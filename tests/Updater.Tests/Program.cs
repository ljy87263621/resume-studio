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
string Base(string name)=>"https://github.com/scj040921/resume-studio/releases/download/v0.6.0/"+name;
stable.Assets=[new() {Name="release-info.json",Url=Base("release-info.json")},new() {Name="release-info.sig",Url=Base("release-info.sig")},new() {Name=info.PackageName,Url=Base(info.PackageName),Digest="sha256:"+info.Sha256,Size=package.Length}];
using var handler=new FixtureHandler(new Dictionary<string,byte[]> {
    ["/repos/scj040921/resume-studio/releases/latest"]=JsonSerializer.SerializeToUtf8Bytes(stable,ReleaseUpdate.Json),
    ["/scj040921/resume-studio/releases/download/v0.6.0/release-info.json"]=bytes,
    ["/scj040921/resume-studio/releases/download/v0.6.0/release-info.sig"]=Sign(bytes),
    ["/scj040921/resume-studio/releases/download/v0.6.0/"+info.PackageName]=package});
using var service=new ReleaseUpdate(pem,handler);
var update=await service.Check("stable","auto",previous,CancellationToken.None);
Check(update is not null&&service.LastRoute.Contains("Proxy"),"direct failure falls back to public proxy");
string download=Path.Combine(root,"download.zip"); await service.Download(update!,"auto",download,null,CancellationToken.None);
Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(download))).ToLowerInvariant()==info.Sha256,"download size and signed hash verified");
string ready=await UpdateInstaller.Prepare(download,Path.Combine(root,"job"),info,CancellationToken.None);
Check(File.Exists(Path.Combine(ready,"ResumeStudio.exe")),"manifest verified before install");
Check(handler.Addresses.All(s=>!s.Contains("Authorization",StringComparison.OrdinalIgnoreCase)),"no account credentials in update routing");
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
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(fixtures[original.AbsolutePath])});
    }
}
