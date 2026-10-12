using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace ResumeStudio.Services;

/// <summary>The desktop client owns one local Java process; no external account is needed.</summary>
public sealed class BackendClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private Process? process;
    public string DataDirectory { get; } = Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumeStudio");
    public async Task StartAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        string token = Guid.NewGuid().ToString("N");
        var root = AppContext.BaseDirectory;
        string java = Path.Combine(root, "runtime", "bin", "java.exe");
        var start = new ProcessStartInfo(java) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = root };
        foreach (var arg in new[] { "--add-modules", "jdk.httpserver", "-jar", Path.Combine(root, "backend", "resume-backend.jar"), "--port", port.ToString(), "--data", DataDirectory, "--token", token }) start.ArgumentList.Add(arg);
        process = Process.Start(start) ?? throw new IOException("无法启动 Java 后端");
        process.ErrorDataReceived += (_, e) => { if(e.Data is not null) Log(e.Data); };
        process.OutputDataReceived += (_, e) => { if(e.Data is not null) Log(e.Data); };
        process.BeginErrorReadLine(); process.BeginOutputReadLine();
        http.BaseAddress = new Uri($"http://127.0.0.1:{port}/api/"); http.DefaultRequestHeaders.Add("X-Resume-Token", token);
        for (int i = 0; i < 60; i++)
        {
            if (process.HasExited) throw new IOException("Java 后端启动失败，请查看 backend.log");
            try { using var response = await http.GetAsync("health"); if(response.IsSuccessStatusCode) return; } catch(HttpRequestException) { }
            await Task.Delay(150);
        }
        throw new IOException("Java 后端启动超时");
    }
    private void Log(string line) { try { File.AppendAllText(Path.Combine(DataDirectory,"backend.log"),$"{DateTimeOffset.Now}: {line}\n"); } catch(IOException) { } }
    public async Task<T> Get<T>(string path)
    {
        using var res = await http.GetAsync(path); await Ensure(res); return await res.Content.ReadFromJsonAsync<T>(Json) ?? throw new IOException("后端响应为空");
    }
    public async Task<T> Save<T>(string path,T document)
    {
        using var res = await http.PostAsJsonAsync(path,document,Json); await Ensure(res); return await res.Content.ReadFromJsonAsync<T>(Json) ?? throw new IOException("保存响应为空");
    }
    public async Task<T> Post<T>(string path,object document)
    {
        using var res=await http.PostAsJsonAsync(path,document,Json); await Ensure(res);
        return await res.Content.ReadFromJsonAsync<T>(Json)??throw new IOException("后端响应为空");
    }
    public async Task Delete(string path) { using var res = await http.DeleteAsync(path); await Ensure(res); }
    private static async Task Ensure(HttpResponseMessage response)
    {
        if(!response.IsSuccessStatusCode) throw new IOException($"后端请求失败 ({(int)response.StatusCode})：{await response.Content.ReadAsStringAsync()}");
    }
    public void Dispose()
    {
        http.Dispose();
        try { if(process is { HasExited:false }) process.Kill(entireProcessTree:true); } catch(InvalidOperationException) { }
        process?.Dispose();
    }
}
