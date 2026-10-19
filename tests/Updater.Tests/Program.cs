using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResumeStudio.Services;

int checks=0;
void Check(bool condition,string label){if(!condition) throw new Exception(label); checks++; Console.WriteLine("PASS: "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e) when(e is IOException or CryptographicException or FormatException){Check(true,label); return;} throw new Exception("Unexpected acceptance: "+label);}
using var key=RSA.Create(3072); string pem=key.ExportSubjectPublicKeyInfoPem();
Check(ReleaseUpdate.Repository=="ljy87263621/resume-studio","updates are pinned to the user's Fork");
Reject(()=>ReleaseUpdate.ValidateUrl("https://github.com/scj040921/resume-studio/releases/download/v1.0.0/app.zip"),"upstream packages cannot replace Fork installations");
Check(ReleaseUpdate.ChannelAddress("nightly").StartsWith("https://github.com/ljy87263621/resume-studio/",StringComparison.Ordinal),"signed channel index belongs to the Fork");
string root=Path.Combine(Path.GetTempPath(),"ResumeUpdater-"+Guid.NewGuid()); Directory.CreateDirectory(root);
var info=new ReleaseIdentity {Channel="stable",Version="0.6.0",Commit=new string('a',40),BuiltAt="2026-10-13T02:00:00Z",Tag="v0.6.0",PackageName="ResumeStudio-v0.6.0-windows-x64.zip",Sha256=new string('0',64)};
var previous=new ReleaseIdentity {Version="0.5.0",Channel="stable",Commit=new string('b',40)};
var stable=new GitHubRelease {Tag="v0.6.0",PublishedAt=DateTimeOffset.UtcNow};
var nightly=new GitHubRelease {Tag="nightly-20261013-aaaaaaaa",Prerelease=true,PublishedAt=DateTimeOffset.UtcNow};
Check(ReleaseUpdate.Select([nightly,stable],"stable")==stable,"Stable excludes Nightly");
Check(ReleaseUpdate.Select([stable,nightly],"nightly")==nightly,"Nightly requires its prerelease tag");
Check(ReleaseUpdate.IsNewer(previous,info),"Stable version increases"); Check(!ReleaseUpdate.IsNewer(info,info),"equal Stable is not an update");
var ni=new ReleaseIdentity {Channel="nightly",Version="0.6.0",Commit=new string('b',40),BuiltAt="2026-10-13T01:00:00Z"};
var nr=new ReleaseIdentity {Channel="nightly",Version="0.6.0",Commit=new string('c',40),BuiltAt="2026-10-13T03:00:00Z"};
Check(ReleaseUpdate.IsNewer(ni,nr)&&!ReleaseUpdate.IsNewer(nr,nr),"Nightly compares source commit and build time");
Check(ReleaseUpdate.IsNewer(ni,info),"explicit Nightly to Stable switch");
Reject(()=>ReleaseUpdate.ValidateUrl("https://github.com/other/app/releases/download/v1/app.zip"),"foreign repository rejected");
Reject(()=>UpdateInstaller.ChildPath(root,"../outside"),"ZIP traversal rejected");
Reject(()=>UpdateInstaller.ChildPath(root,"C:/outside"),"absolute ZIP path rejected");
byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(info,ReleaseUpdate.Json);
byte[] Sign(byte[] data)=>Encoding.UTF8.GetBytes(Convert.ToBase64String(key.SignData(data,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)));
Check(ReleaseUpdate.VerifyInfo(bytes,Sign(bytes),pem,"stable","v0.6.0").Commit==info.Commit,"signed metadata accepted");
Check(ReleaseUpdate.ChannelAddress("stable").EndsWith("/desktop-stable.json"),"desktop reads only its own platform index");
info.Platform="android";var androidInfo=JsonSerializer.SerializeToUtf8Bytes(info,ReleaseUpdate.Json);
Reject(()=>ReleaseUpdate.VerifyInfo(androidInfo,Sign(androidInfo),pem,"stable","v0.6.0"),"valid Android signature cannot cross into desktop updater");info.Platform="";
var altered=(byte[])bytes.Clone(); altered[20]^=1; Reject(()=>ReleaseUpdate.VerifyInfo(altered,Sign(bytes),pem,"stable","v0.6.0"),"metadata tampering rejected");
Reject(()=>ReleaseUpdate.VerifyInfo(bytes,Sign(bytes),pem,"nightly","v0.6.0"),"signature cannot cross channels");

string archive=Path.Combine(root,"fixture.zip");
var files=new Dictionary<string,byte[]>();
foreach(string path in new[] {"ResumeStudio.exe","ResumeStudio.dll","Assets/AppIcon.ico","runtime/bin/java.exe","backend/resume-backend.jar"}) files[path]=Encoding.UTF8.GetBytes("isolated fixture "+path);
files["Assets/ReleaseIdentity.json"]=JsonSerializer.SerializeToUtf8Bytes(info,ReleaseUpdate.Json);
var manifest=new PayloadManifest {Files=files.Select(p=>new PayloadFile(p.Key,Convert.ToHexString(SHA256.HashData(p.Value)).ToLowerInvariant())).ToList()};
using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){
    foreach(var file in files){using var stream=zip.CreateEntry("ResumeStudio/"+file.Key).Open(); stream.Write(file.Value);}
    using var entry=zip.CreateEntry("ResumeStudio/distribution-manifest.json").Open(); entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest,ReleaseUpdate.Json));
}
var package=await File.ReadAllBytesAsync(archive); info.Sha256=Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
bytes=JsonSerializer.SerializeToUtf8Bytes(info,ReleaseUpdate.Json);
string Base(string name)=>"https://github.com/ljy87263621/resume-studio/releases/download/v0.6.0/"+name;
stable.Assets=[new() {Name="release-info.json",Url=Base("release-info.json")},new() {Name="release-info.sig",Url=Base("release-info.sig")},new() {Name=info.PackageName,Url=Base(info.PackageName),Digest="sha256:"+info.Sha256,Size=package.Length}];
using var handler=new FixtureHandler(new Dictionary<string,byte[]> {
    ["/repos/ljy87263621/resume-studio/releases/latest"]=JsonSerializer.SerializeToUtf8Bytes(stable,ReleaseUpdate.Json),
    ["/ljy87263621/resume-studio/releases/download/v0.6.0/release-info.json"]=bytes,
    ["/ljy87263621/resume-studio/releases/download/v0.6.0/release-info.sig"]=Sign(bytes),
    ["/ljy87263621/resume-studio/releases/download/v0.6.0/"+info.PackageName]=package});
using var service=new ReleaseUpdate(pem,handler);
var update=await service.Check("stable","auto",previous,CancellationToken.None);
Check(update is not null&&service.LastRoute.Contains("Proxy"),"direct failure falls back to public proxy");
string download=Path.Combine(root,"download.zip"); await service.Download(update!,"auto",download,null,CancellationToken.None);
Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(download))).ToLowerInvariant()==info.Sha256,"download size and signed hash verified");
string ready=await UpdateInstaller.Prepare(download,Path.Combine(root,"job"),info,CancellationToken.None);
Check(File.Exists(Path.Combine(ready,"ResumeStudio.exe")),"manifest verified before install");
Check(handler.Addresses.All(s=>!s.Contains("Authorization",StringComparison.OrdinalIgnoreCase)),"no account credentials in update routing");
var channelInfo=new ReleaseIdentity {Channel="nightly",Version="0.6.0",Commit=new string('c',40),BuiltAt="2026-10-13T03:00:00Z",Tag="nightly-20261013-cccccccc",PackageName="ResumeStudio-v0.6.0-nightly.20261013.cccccccc-windows-x64.zip",PackageSize=package.Length,Sha256=info.Sha256};
byte[] Envelope(ReleaseIdentity identity){var data=JsonSerializer.SerializeToUtf8Bytes(identity,ReleaseUpdate.Json);return JsonSerializer.SerializeToUtf8Bytes(new {data=Convert.ToBase64String(data),signature=Encoding.UTF8.GetString(Sign(data))},ReleaseUpdate.Json);}
var envelope=Envelope(channelInfo);var indexed=ReleaseUpdate.VerifyChannel(envelope,pem,"nightly");
info.PackageSize=package.Length;Check(ReleaseUpdate.VerifyChannel(Envelope(info),pem,"stable").Identity.Tag=="v0.6.0","Stable signed channel remains compatible with its formal release tag");
Check(indexed.Identity.Commit==channelInfo.Commit&&indexed.Package.Size==package.Length&&indexed.Package.Url.Contains("/nightly-20261013-cccccccc/"),"signed Nightly channel yields an official package without REST metadata");
Reject(()=>ReleaseUpdate.VerifyChannel(envelope,pem,"stable"),"signed channel cannot cross Stable and Nightly");
var tampered=JsonSerializer.SerializeToUtf8Bytes(new {data=Convert.ToBase64String(bytes),signature=Encoding.UTF8.GetString(Sign(JsonSerializer.SerializeToUtf8Bytes(channelInfo,ReleaseUpdate.Json)))},ReleaseUpdate.Json);
Reject(()=>ReleaseUpdate.VerifyChannel(tampered,pem,"nightly"),"tampered channel payload is rejected");
channelInfo.Tag="nightly-20261013-aaaaaaaa";Reject(()=>ReleaseUpdate.VerifyChannel(Envelope(channelInfo),pem,"nightly"),"channel tag must match source commit");channelInfo.Tag="nightly-20261013-cccccccc";
channelInfo.PackageName="ResumeStudio-v0.6.0-nightly.20261013.aaaaaaaa-windows-x64.zip";Reject(()=>ReleaseUpdate.VerifyChannel(Envelope(channelInfo),pem,"nightly"),"channel package must match date and source commit");channelInfo.PackageName="ResumeStudio-v0.6.0-nightly.20261013.cccccccc-windows-x64.zip";
channelInfo.PackageSize=0;Reject(()=>ReleaseUpdate.VerifyChannel(Envelope(channelInfo),pem,"nightly"),"unsigned or missing package size cannot enter a channel");channelInfo.PackageSize=package.Length;
using var channelHandler=new ChannelHandler(envelope,package);using var channelService=new ReleaseUpdate(pem,channelHandler);
var channelUpdate=await channelService.Check("nightly","auto",ni,CancellationToken.None);
Check(channelUpdate is not null&&channelService.LastSource=="signed-channel"&&channelHandler.Addresses.All(u=>u.Host!="api.github.com"),"Nightly update succeeds while every REST endpoint is rate limited");
string channelDownload=Path.Combine(root,"channel-download.zip");await channelService.Download(channelUpdate!,"auto",channelDownload,null,CancellationToken.None);
Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(channelDownload))).ToLowerInvariant()==channelInfo.Sha256,"channel package download checks signed size and hash");
Check(await channelService.Check("nightly","auto",channelInfo,CancellationToken.None) is null,"same signed channel source does not repeat update prompt");
var newer=new ReleaseIdentity {Channel="nightly",Version="0.7.0",Commit=new string('d',40),BuiltAt="2026-10-14T03:00:00Z"};
Check(await channelService.Check("nightly","auto",newer,CancellationToken.None) is null,"stale channel cannot roll a newer client back");
using var proxyChannelHandler=new ChannelHandler(envelope,package,true);using var proxyChannelService=new ReleaseUpdate(pem,proxyChannelHandler);
Check(await proxyChannelService.Check("nightly","proxy",ni,CancellationToken.None) is not null&&proxyChannelService.LastRoute.Contains("Proxy"),"proxy-only channel works without an API account token");
using var badChannelHandler=new ChannelHandler(tampered,package);using var badChannelService=new ReleaseUpdate(pem,badChannelHandler);
try{await badChannelService.Check("nightly","auto",ni,CancellationToken.None);throw new Exception("Bad channel accepted");}catch(IOException){Check(badChannelHandler.Addresses.All(u=>u.Host!="api.github.com"),"invalid channel signature stops rather than falling back to another index");}
using var missingChannelHandler=new ChannelHandler(null,package);using var missingChannelService=new ReleaseUpdate(pem,missingChannelHandler);
try{await missingChannelService.Check("nightly","auto",ni,CancellationToken.None);throw new Exception("API failure ignored");}catch(IOException){Check(missingChannelHandler.Addresses.Any(u=>u.Host=="api.github.com"),"missing channel can attempt the legacy API and reports its failure");}
string bad=Path.Combine(root,"bad.zip"); using(var zip=ZipFile.Open(bad,ZipArchiveMode.Create)){using var entry=zip.CreateEntry("../outside.txt").Open(); entry.WriteByte(1);}
try{await UpdateInstaller.Prepare(bad,Path.Combine(root,"badjob"),info,CancellationToken.None); throw new Exception("ZIP escaped");}catch(IOException){Check(true,"malformed ZIP rejected during extraction");}
Console.WriteLine($"PASS: {checks} updater checks; fixtures: {root}");

sealed class FixtureHandler(Dictionary<string,byte[]> fixtures):HttpMessageHandler
{
    public List<string> Addresses {get;}=[];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        string url=request.RequestUri!.AbsoluteUri; Addresses.Add(url);
        if(request.RequestUri.Host!="gh-proxy.org") throw new HttpRequestException("isolated direct-connection failure");
        string path=request.RequestUri.AbsolutePath; int scheme=path.IndexOf("https://",StringComparison.Ordinal); var original=new Uri(path[scheme..]);
        return Task.FromResult(fixtures.TryGetValue(original.AbsolutePath,out var bytes)?new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(bytes)}:new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

sealed class ChannelHandler(byte[]? index,byte[] package,bool proxyOnly=false):HttpMessageHandler
{
    public List<Uri> Addresses {get;}=[];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        var url=request.RequestUri!;Addresses.Add(url);
        if(proxyOnly&&url.Host!="gh-proxy.org")throw new HttpRequestException("isolated direct failure");
        var original=url.Host=="gh-proxy.org"?new Uri(url.AbsolutePath[url.AbsolutePath.IndexOf("https://",StringComparison.Ordinal)..]+url.Query):url;
        if(original.Host=="api.github.com")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        byte[]? body=original.AbsolutePath.EndsWith("/desktop-nightly.json",StringComparison.Ordinal)?index:original.AbsolutePath.EndsWith(".zip",StringComparison.Ordinal)?package:null;
        return Task.FromResult(body is null?new HttpResponseMessage(HttpStatusCode.NotFound):new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(body)});
    }
}
