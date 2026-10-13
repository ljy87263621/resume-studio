using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResumeStudio.Services;

public sealed class ReleaseIdentity
{
    public string Channel {get;set;}="stable";
    public string Version {get;set;}="0.0.0";
    public string Commit {get;set;}="";
    public string BuiltAt {get;set;}="";
    public string Tag {get;set;}="";
    public string PackageName {get;set;}="";
    public string Sha256 {get;set;}="";
    public long PackageSize {get;set;}
    public string ChangeLogName {get;set;}="";
    public string ChangeLogSha256 {get;set;}="";
    public long ChangeLogSize {get;set;}
    public static ReleaseIdentity Current(string root)
    {
        string file=Path.Combine(root,"Assets","ReleaseIdentity.json");
        return File.Exists(file)?JsonSerializer.Deserialize<ReleaseIdentity>(File.ReadAllText(file),ReleaseUpdate.Json)??new():new() {Version=typeof(ReleaseIdentity).Assembly.GetName().Version?.ToString(3)??"0.0.0"};
    }
    public string Display=>Channel=="nightly"?$"Nightly {Version} · {Commit[..Math.Min(8,Commit.Length)]}":$"Stable {Version}";
}
public sealed class GitHubAsset
{
    public string Name {get;set;}="";
    [JsonPropertyName("browser_download_url")] public string Url {get;set;}="";
    public string Digest {get;set;}="";
    public long Size {get;set;}
}
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string Tag {get;set;}="";
    [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt {get;set;}
    [JsonPropertyName("html_url")] public string Url {get;set;}="";
    public string Body {get;set;}="";
    public bool Draft {get;set;}
    public bool Prerelease {get;set;}
    public List<GitHubAsset> Assets {get;set;}=[];
}
public sealed record AvailableUpdate(GitHubRelease Release,ReleaseIdentity Identity,GitHubAsset Package);
public sealed class ReleaseNote
{
    public string Version {get;set;}="";
    public string Body {get;set;}="";
}
public sealed class ReleaseChangeLog
{
    public int Schema {get;set;}
    public string Channel {get;set;}="";
    public string Version {get;set;}="";
    public string Commit {get;set;}="";
    public string Tag {get;set;}="";
    public List<ReleaseNote> Releases {get;set;}=[];
}

/// <summary>Only this public repository is accepted. Proxy transport never receives personal data or API credentials.</summary>
public sealed class ReleaseUpdate : IDisposable
{
    public const string Repository="scj040921/resume-studio";
    public const string ProxyPrefix="https://gh-proxy.org/";
    public static readonly JsonSerializerOptions Json=new() {PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    private readonly HttpClient http;
    private readonly string publicKey;
    public string LastRoute {get;private set;}="";
    public string LastSource {get;private set;}="";
    public AvailableUpdate? LatestCheckedRelease {get;private set;}
    public IReadOnlyList<ReleaseNote> ChangeLog {get;private set;}=[];
    public string ChangeLogError {get;private set;}="";
    public ReleaseUpdate(string key,HttpMessageHandler? handler=null)
    {
        publicKey=key;
        http=new HttpClient(handler??new HttpClientHandler {AllowAutoRedirect=true,MaxAutomaticRedirections=4}) {Timeout=Timeout.InfiniteTimeSpan};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ResumeStudio-Update/0.6");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }
    public static Uri ValidateUrl(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo!=""||u.Fragment!=""||!u.IsDefaultPort) throw new IOException("更新地址无效。");
        bool allowed=u.Host=="api.github.com"&&u.AbsolutePath.StartsWith("/repos/"+Repository+"/releases",StringComparison.Ordinal)
            ||u.Host=="github.com"&&u.AbsolutePath.StartsWith("/"+Repository+"/releases/",StringComparison.Ordinal);
        if(!allowed) throw new IOException("更新地址不属于拾简官方仓库。"); return u;
    }
    private static IEnumerable<(Uri Url,string Name)> Routes(string address,string route)
    {
        var uri=ValidateUrl(address);
        if(route!="proxy") yield return (uri,"GitHub 直连");
        if(route!="direct") yield return (new Uri(ProxyPrefix+uri.AbsoluteUri),"GH-Proxy 公益代理");
    }
    private async Task<byte[]> Read(string address,string route,int maximum,CancellationToken token)
    {
        Exception? failure=null;
        foreach(var target in Routes(address,route)) {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(18));
            try {
                using var response=await http.GetAsync(target.Url,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                if(!response.IsSuccessStatusCode) throw new IOException($"更新请求失败（HTTP {(int)response.StatusCode}）。");
                await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer=new MemoryStream(); var chunk=new byte[8192]; int count;
                while((count=await stream.ReadAsync(chunk,timeout.Token))>0) {if(buffer.Length+count>maximum) throw new IOException("更新响应超出大小限制。"); await buffer.WriteAsync(chunk.AsMemory(0,count),timeout.Token);}
                LastRoute=target.Name; return buffer.ToArray();
            }
            catch(Exception e) when(!token.IsCancellationRequested&&e is HttpRequestException or IOException or OperationCanceledException) {failure=e;}
        }
        token.ThrowIfCancellationRequested(); throw new IOException("无法连接 GitHub Releases，请重试或切换下载线路。",failure);
    }
    public static GitHubRelease? Select(IEnumerable<GitHubRelease> releases,string channel)
    {
        var published=releases.Where(r=>!r.Draft);
        if(channel=="nightly") return published.Where(r=>r.Prerelease&&Regex.IsMatch(r.Tag,@"^nightly-\d{8}-[0-9a-f]{8}$")).OrderByDescending(r=>r.PublishedAt).FirstOrDefault();
        return published.Where(r=>!r.Prerelease&&Regex.IsMatch(r.Tag,@"^v\d+\.\d+\.\d+$")).OrderByDescending(r=>System.Version.Parse(r.Tag[1..])).FirstOrDefault();
    }
    public static bool IsNewer(ReleaseIdentity current,ReleaseIdentity remote)
    {
        if(!System.Version.TryParse(current.Version,out var installed)||!System.Version.TryParse(remote.Version,out var available)) return false;
        if(current.Channel!=remote.Channel) return remote.Channel=="stable"||available>=installed;
        return remote.Channel=="stable"?available>installed:available>=installed&&remote.Commit!=current.Commit&&DateTimeOffset.TryParse(remote.BuiltAt,out var newer)&&(!DateTimeOffset.TryParse(current.BuiltAt,out var older)||newer>older);
    }
    public static ReleaseIdentity VerifyInfo(byte[] data,byte[] signature,string publicKey,string channel,string tag)
    {
        using var rsa=RSA.Create(); rsa.ImportFromPem(publicKey);
        if(!rsa.VerifyData(data,Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(signature).Trim()),HashAlgorithmName.SHA256,RSASignaturePadding.Pss)) throw new IOException("更新签名校验失败，停止更新。");
        var info=JsonSerializer.Deserialize<ReleaseIdentity>(data,Json)??throw new IOException("更新说明无效。");
        if(info.Channel!=channel||info.Tag!=tag||!Regex.IsMatch(info.Version,@"^\d+\.\d+\.\d+$")||!Regex.IsMatch(info.Commit,@"^[0-9a-f]{40}$")||!Regex.IsMatch(info.Sha256,@"^[0-9a-f]{64}$")||!DateTimeOffset.TryParse(info.BuiltAt,out _)
            ||!Regex.IsMatch(info.PackageName,@"^ResumeStudio-v\d+\.\d+\.\d+(?:-nightly\.\d{8}\.[0-9a-f]{8})?-windows-x64\.zip$")) throw new IOException("更新说明的版本或通道不匹配。");
        if((info.ChangeLogName!=""||info.ChangeLogSha256!=""||info.ChangeLogSize!=0)&&
            (info.ChangeLogName!="release-changelog.json"||!Regex.IsMatch(info.ChangeLogSha256,@"^[0-9a-f]{64}$")||info.ChangeLogSize<=0||info.ChangeLogSize>256000))throw new IOException("更新日志的签名信息无效。");
        return info;
    }
    public static string ChannelAddress(string channel)
    {
        if(channel is not "stable" and not "nightly")throw new IOException("更新通道无效。");
        return $"https://github.com/{Repository}/releases/download/update-channels/{channel}.json";
    }
    public static AvailableUpdate VerifyChannel(byte[] envelope,string publicKey,string channel)
    {
        if(envelope.Length>64000)throw new IOException("通道索引超出大小限制。");
        using var document=JsonDocument.Parse(envelope);var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("data",out var content)||content.ValueKind!=JsonValueKind.String||!root.TryGetProperty("signature",out var signature)||signature.ValueKind!=JsonValueKind.String)throw new IOException("通道索引格式无效。");
        byte[] data=Convert.FromBase64String(content.GetString()!);
        if(data.Length>16000)throw new IOException("通道索引内容过长。");
        var candidate=JsonSerializer.Deserialize<ReleaseIdentity>(data,Json)??throw new IOException("通道索引为空。");
        var info=VerifyInfo(data,System.Text.Encoding.UTF8.GetBytes(signature.GetString()!),publicKey,channel,candidate.Tag);
        bool validTag=channel=="stable"?info.Tag=="v"+info.Version:Regex.IsMatch(info.Tag,@"^nightly-\d{8}-[0-9a-f]{8}$")&&info.Tag.EndsWith(info.Commit[..8],StringComparison.Ordinal);
        if(!validTag||info.PackageSize<=0||info.PackageSize>512_000_000)throw new IOException("通道索引的标签或安装包大小无效。");
        string expected=channel=="stable"?$"ResumeStudio-v{info.Version}-windows-x64.zip":$"ResumeStudio-v{info.Version}-nightly.{info.Tag.Split('-')[1]}.{info.Commit[..8]}-windows-x64.zip";
        if(info.PackageName!=expected)throw new IOException("通道索引的安装包名称不匹配。");
        string url=$"https://github.com/{Repository}/releases/download/{info.Tag}/{info.PackageName}";ValidateUrl(url);
        var package=new GitHubAsset {Name=info.PackageName,Url=url,Digest="sha256:"+info.Sha256,Size=info.PackageSize};
        var release=new GitHubRelease {Tag=info.Tag,Prerelease=channel=="nightly",PublishedAt=DateTimeOffset.Parse(info.BuiltAt),Url=$"https://github.com/{Repository}/releases/tag/{info.Tag}",Assets=[package]};
        return new(release,info,package);
    }
    public async Task<AvailableUpdate?> Check(string channel,string route,ReleaseIdentity current,CancellationToken token)
    {
        LastSource="";LatestCheckedRelease=null;ChangeLog=[];ChangeLogError="";
        string channelAddress=ChannelAddress(channel);
        byte[]? channelBytes=null;
        // One signed, public Release asset avoids the anonymous REST API quota. An invalid
        // signature is fatal; only transport failure may fall back to the legacy API.
        try {channelBytes=await Read(channelAddress+"?check="+DateTimeOffset.UtcNow.ToUnixTimeSeconds(),route,64000,token);}
        catch(IOException) when(!token.IsCancellationRequested) { }
        if(channelBytes is not null){var indexed=VerifyChannel(channelBytes,publicKey,channel);LastSource="signed-channel";await ReadChangeLog(indexed,current,route,token);return IsNewer(current,indexed.Identity)?indexed:null;}
        LastSource="release-api";
        GitHubRelease? release;
        if(channel=="stable") {
            var bytes=await Read($"https://api.github.com/repos/{Repository}/releases/latest",route,2_000_000,token);
            release=Select([JsonSerializer.Deserialize<GitHubRelease>(bytes,Json)??throw new IOException("发行信息为空。")],channel);
        } else {
            var bytes=await Read($"https://api.github.com/repos/{Repository}/releases?per_page=100",route,8_000_000,token);
            release=Select(JsonSerializer.Deserialize<List<GitHubRelease>>(bytes,Json)??[],channel);
        }
        if(release is null) return null;
        var metadata=release.Assets.SingleOrDefault(a=>a.Name=="release-info.json"); var signature=release.Assets.SingleOrDefault(a=>a.Name=="release-info.sig");
        if(metadata is null||signature is null) {
            if(channel=="stable"&&System.Version.TryParse(release.Tag.TrimStart('v'),out var legacy)&&legacy<=System.Version.Parse(current.Version)) return null;
            throw new IOException("此发行版未提供可验证的更新签名，请前往官网手动下载。");
        }
        var data=await Read(metadata.Url,route,64000,token); var sig=await Read(signature.Url,route,8192,token);
        var info=VerifyInfo(data,sig,publicKey,channel,release.Tag);
        var package=release.Assets.SingleOrDefault(a=>a.Name==info.PackageName)??throw new IOException("发行版缺少 Windows 更新包。"); ValidateUrl(package.Url);
        if(package.Size<=0||package.Size>512_000_000||package.Digest!="sha256:"+info.Sha256) throw new IOException("更新包摘要与签名不一致。");
        var checkedRelease=new AvailableUpdate(release,info,package);await ReadChangeLog(checkedRelease,current,route,token);
        return IsNewer(current,info)?checkedRelease:null;
    }

    public static IReadOnlyList<ReleaseNote> VerifyChangeLog(byte[] bytes,ReleaseIdentity expected,ReleaseIdentity current)
    {
        if(bytes.Length!=expected.ChangeLogSize||Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=expected.ChangeLogSha256)throw new IOException("更新日志校验失败。");
        var log=JsonSerializer.Deserialize<ReleaseChangeLog>(bytes,Json)??throw new IOException("更新日志为空。");
        if(log.Schema!=1||log.Channel!=expected.Channel||log.Version!=expected.Version||log.Commit!=expected.Commit||log.Tag!=expected.Tag||log.Releases is null||log.Releases.Count>200)throw new IOException("更新日志与发行版不匹配。");
        var versions=new HashSet<string>(StringComparer.Ordinal);
        foreach(var note in log.Releases)
            if(note is null||string.IsNullOrWhiteSpace(note.Version)||!Regex.IsMatch(note.Version,@"^\d+\.\d+\.\d+$")||!versions.Add(note.Version)||string.IsNullOrWhiteSpace(note.Body)||note.Body.Length>16000||System.Version.Parse(note.Version)>System.Version.Parse(expected.Version))throw new IOException("更新日志条目无效。");
        if(!versions.Contains(expected.Version))throw new IOException("更新日志缺少最新版本的说明。");
        var latest=System.Version.Parse(expected.Version);var installed=System.Version.TryParse(current.Version,out var parsed)?parsed:new System.Version(0,0,0);
        // Include the installed version as the labelled lower boundary, newest first.
        return log.Releases.Where(note=>System.Version.Parse(note.Version)<=latest&&System.Version.Parse(note.Version)>=(installed>latest?latest:installed)).OrderByDescending(note=>System.Version.Parse(note.Version)).ToList();
    }

    private async Task ReadChangeLog(AvailableUpdate release,ReleaseIdentity current,string route,CancellationToken token)
    {
        LatestCheckedRelease=release;
        if(release.Identity.ChangeLogName=="") {ChangeLogError="此旧版发行未附带完整更新日志，可查看发行记录。";return;}
        try {
            string address=$"https://github.com/{Repository}/releases/download/{release.Identity.Tag}/{release.Identity.ChangeLogName}";
            var bytes=await Read(address,route,256000,token);ChangeLog=VerifyChangeLog(bytes,release.Identity,current);
            if(ChangeLog.Count==0)ChangeLogError="此发行包没有当前版本区间的日志，可查看发行记录。";
        } catch(Exception error) when(!token.IsCancellationRequested&&error is IOException or HttpRequestException or JsonException or FormatException) {
            // Release notes are informational. Their failure never bypasses package signature checks.
            ChangeLogError="更新日志读取未完成："+error.Message;
        }
    }
    public async Task Download(AvailableUpdate update,string route,string path,IProgress<double>? progress,CancellationToken token)
    {
        Exception? failure=null;
        foreach(var target in Routes(update.Package.Url,route)) {
            try {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
                using var response=await http.GetAsync(target.Url,HttpCompletionOption.ResponseHeadersRead,timeout.Token); response.EnsureSuccessStatusCode();
                await using var input=await response.Content.ReadAsStreamAsync(token); await using var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
                var bytes=new byte[81920]; long total=0; int read;
                while(true) {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30)); read=await input.ReadAsync(bytes,timeout.Token); if(read==0) break;
                    total+=read; if(total>update.Package.Size) throw new IOException("更新包长度异常。"); await output.WriteAsync(bytes.AsMemory(0,read),token); progress?.Report(100d*total/update.Package.Size);
                }
                await output.FlushAsync(token); if(total!=update.Package.Size) throw new IOException("更新包下载不完整。");
                LastRoute=target.Name;
            }
            catch(Exception e) when(!token.IsCancellationRequested&&e is IOException or HttpRequestException or OperationCanceledException) {failure=e; continue;}
            await using var check=File.OpenRead(path); string hash=Convert.ToHexString(await SHA256.HashDataAsync(check,token)).ToLowerInvariant();
            if(hash!=update.Identity.Sha256) {failure=new IOException("更新包哈希校验失败。"); continue;} return;
        }
        token.ThrowIfCancellationRequested(); throw new IOException("更新包未下载成功；当前程序保持原样。",failure);
    }
    public void Dispose()=>http.Dispose();
}
