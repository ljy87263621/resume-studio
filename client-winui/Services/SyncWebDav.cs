using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ResumeStudio.Services;

public sealed record SyncRemote(byte[] Bytes,string ETag);
/// <summary>GitHub can implement this conditional read/write contract without changing the protocol.</summary>
public interface ISyncRemoteStore
{
    Task<SyncRemote?> ReadAsync(CancellationToken cancellationToken);
    Task<string> WriteAsync(byte[] bytes,string? expectedETag,CancellationToken cancellationToken);
}

public sealed class SyncWebDav : ISyncRemoteStore,IDisposable
{
    private readonly HttpClient client;
    private readonly Uri file;
    private readonly TimeSpan operationTimeout;
    public static Uri Address(string directory)
    {
        if(!Uri.TryCreate(directory.Trim(),UriKind.Absolute,out var uri)||string.IsNullOrEmpty(uri.Host)||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||
           (uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback))) throw new IOException("请填写 HTTPS WebDAV 目录地址；离线测试可用本机 HTTP。");
        if(Uri.UnescapeDataString(uri.AbsolutePath).Split('/').Any(p=>p is "." or ".."||p.Contains('\\'))) throw new IOException("WebDAV 目录路径无效。");
        return new Uri(uri.AbsoluteUri.TrimEnd('/')+"/");
    }
    public SyncWebDav(Uri directory,string user,string password,TimeSpan? timeout=null)
    {
        file=new Uri(Address(directory.AbsoluteUri),"resume-studio.sync.json");
        operationTimeout=timeout??TimeSpan.FromSeconds(90);
        if(operationTimeout<=TimeSpan.Zero||operationTimeout.TotalMilliseconds>int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
        client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=Timeout.InfiniteTimeSpan};
        if(user.Contains(':')||user.Contains('\r')||user.Contains('\n')) throw new IOException("WebDAV 用户名格式无效。");
        if(user!=""||password!="") client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(user+":"+password)));
    }
    private static void Ensure(HttpResponseMessage response)
    {
        if((int)response.StatusCode is >=300 and <400) throw new IOException("WebDAV 地址返回重定向，请填写最终目录地址后重试。");
        if(response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict) throw new IOException("云端资料已改变，条件写入已拒绝。请重新检查同步。");
        if(!response.IsSuccessStatusCode) throw new IOException($"WebDAV 请求失败（HTTP {(int)response.StatusCode}），请检查目录和凭据。");
    }
    private static string StrongETag(HttpResponseMessage response)
    {
        var etag=response.Headers.ETag;
        if(etag is null||etag.IsWeak) throw new IOException("WebDAV 服务未提供强 ETag，无法安全检查并发修改，请使用支持 ETag 的服务。");
        return etag.ToString();
    }
    public async Task<SyncRemote?> ReadAsync(CancellationToken cancellationToken)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(operationTimeout);
        try { return await ReadCoreAsync(deadline.Token); }
        catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested&&deadline.IsCancellationRequested) { throw new IOException("WebDAV 读取超时，请检查网络后重新同步。",ex); }
    }
    private async Task<SyncRemote?> ReadCoreAsync(CancellationToken cancellationToken)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,file); request.Headers.CacheControl=new CacheControlHeaderValue {NoCache=true};
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode==HttpStatusCode.NotFound) return null; Ensure(response); string etag=StrongETag(response);
        if(response.Content.Headers.ContentLength>SyncProtocol.MaxBytes) throw new IOException("同步包超过 64 MiB。");
        using var stream=await response.Content.ReadAsStreamAsync(cancellationToken); using var buffer=new MemoryStream(); byte[] block=new byte[8192];
        int read; while((read=await stream.ReadAsync(block,cancellationToken))>0) { if(buffer.Length+read>SyncProtocol.MaxBytes) throw new IOException("同步包超过 64 MiB。"); await buffer.WriteAsync(block.AsMemory(0,read),cancellationToken); }
        return new(buffer.ToArray(),etag);
    }
    public async Task<string> WriteAsync(byte[] bytes,string? expectedETag,CancellationToken cancellationToken)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(operationTimeout);
        try { return await WriteCoreAsync(bytes,expectedETag,deadline.Token); }
        catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested&&deadline.IsCancellationRequested) { throw new IOException("WebDAV 上传或回读验证超时，云端可能已写入；请重新检查同步。",ex); }
    }
    private async Task<string> WriteCoreAsync(byte[] bytes,string? expectedETag,CancellationToken cancellationToken)
    {
        if(bytes.Length>SyncProtocol.MaxBytes) throw new IOException("同步包超过 64 MiB。");
        using var request=new HttpRequestMessage(HttpMethod.Put,file) {Content=new ByteArrayContent(bytes)};
        request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json") {CharSet="utf-8"};
        if(expectedETag is null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        else { if(!EntityTagHeaderValue.TryParse(expectedETag,out var tag)||tag.IsWeak) throw new IOException("云端 ETag 无效。"); request.Headers.IfMatch.Add(tag); }
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken); Ensure(response);
        // Some DAV servers omit the PUT response ETag. Read back to verify the exact committed bytes.
        var committed=await ReadCoreAsync(cancellationToken);
        if(committed is null||!committed.Bytes.AsSpan().SequenceEqual(bytes)) throw new IOException("云端写入后的内容不一致，请重新检查同步；本次不建立同步基线。");
        return committed.ETag;
    }
    public void Dispose()=>client.Dispose();
}
