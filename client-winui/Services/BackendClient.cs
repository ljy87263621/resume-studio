using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ResumeStudio.Services;

/// <summary>The desktop client owns one local Java process; no external account is needed.</summary>
public sealed class BackendClient : IDisposable
{
    private const long MaxLogBytes = 256 * 1024;
    private static readonly object LogLock = new();
    public static string DefaultDataDirectory { get; } = ResolveDataDirectory();
    private static string ResolveDataDirectory()
    {
        var specified=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_DATA");
        if(!string.IsNullOrWhiteSpace(specified)) return specified;
        bool test=Environment.GetCommandLineArgs().Any(arg=>arg is "--self-test" or "--benchmark" or "--iteration-test" or "--feature-test" or "--appearance-review" or "--template-assets" or "--navigation-test");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),test?"ResumeStudioVerification":"ResumeStudio",test?Guid.NewGuid().ToString("N"):"");
    }
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, WriteIndented = true };
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private Process? process;
    private Task? startup;
    public string DataDirectory { get; } = DefaultDataDirectory;
    public Task StartAsync() => startup ??= StartCoreAsync();
    private async Task StartCoreAsync()
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
    private void Log(string line)
    {
        try
        {
            lock(LogLock)
            {
                Directory.CreateDirectory(DataDirectory);
                string path=Path.Combine(DataDirectory,"backend.log"),previous=path+".1";
                string entry=$"{DateTimeOffset.Now}: {line}{Environment.NewLine}";
                if(File.Exists(path)&&new FileInfo(path).Length+Encoding.UTF8.GetByteCount(entry)>MaxLogBytes)
                {
                    File.Delete(previous); File.Move(path,previous);
                }
                File.AppendAllText(path,entry,Encoding.UTF8);
            }
        }
        catch(IOException) { }
        catch(UnauthorizedAccessException) { }
    }
    public async Task<T> Get<T>(string path,CancellationToken cancellationToken=default)
    {
        await StartAsync().WaitAsync(cancellationToken);
        using var res = await http.GetAsync(path,cancellationToken); await Ensure(res); return await res.Content.ReadFromJsonAsync<T>(Json,cancellationToken) ?? throw new IOException("后端响应为空");
    }
    public async Task<T> Save<T>(string path,T document)
    {
        await StartAsync();
        using var res = await http.PostAsJsonAsync(path,document,Json); await Ensure(res); return await res.Content.ReadFromJsonAsync<T>(Json) ?? throw new IOException("保存响应为空");
    }
    public async Task<T> Post<T>(string path,object document)
    {
        await StartAsync();
        using var res=await http.PostAsJsonAsync(path,document,Json); await Ensure(res);
        return await res.Content.ReadFromJsonAsync<T>(Json)??throw new IOException("后端响应为空");
    }
    public async Task Delete(string path) { await StartAsync(); using var res = await http.DeleteAsync(path); await Ensure(res); }
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
