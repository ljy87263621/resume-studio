using ResumeStudio.Services;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
void Reject(Action action,string label){try {action();throw new Exception("Not rejected: "+label);}catch(IOException){Check(true,label);}}
var identity=new ReleaseIdentity {Channel="stable",Version="0.7.6",Commit=new string('a',40),Tag="v0.7.6",BuiltAt="2026-10-13T00:00:00Z"};
var notes=new ReleaseChangeLog {Schema=1,Channel=identity.Channel,Version=identity.Version,Commit=identity.Commit,Tag=identity.Tag,Releases=[new() {Version="0.7.3",Body="older"},new() {Version="0.7.6",Body="latest"},new() {Version="0.7.5",Body="installed"},new() {Version="0.7.4",Body="between"}]};
byte[] NoteBytes(){var bytes=JsonSerializer.SerializeToUtf8Bytes(notes,ReleaseUpdate.Json);identity.ChangeLogName="release-changelog.json";identity.ChangeLogSize=bytes.Length;identity.ChangeLogSha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();return bytes;}
var bytes=NoteBytes();var current=new ReleaseIdentity {Version="0.7.4"};
Check(ReleaseUpdate.VerifyChangeLog(bytes,identity,current).Select(note=>note.Version).SequenceEqual(new[] {"0.7.6","0.7.5","0.7.4"}),"changelog includes every version down to the installed boundary, newest first");
Check(ReleaseUpdate.VerifyChangeLog(bytes,identity,new() {Version="0.7.6"}).Count==1,"already-current checks still expose the current release notes");
var altered=bytes.ToArray();altered[^2]^=1;Reject(()=>ReleaseUpdate.VerifyChangeLog(altered,identity,current),"altered changelog bytes are rejected");
notes.Commit=new string('b',40);bytes=NoteBytes();Reject(()=>ReleaseUpdate.VerifyChangeLog(bytes,identity,current),"changelog must match the signed source commit");notes.Commit=identity.Commit;
notes.Releases.Add(new() {Version="0.7.6",Body="duplicate"});bytes=NoteBytes();Reject(()=>ReleaseUpdate.VerifyChangeLog(bytes,identity,current),"duplicate changelog versions are rejected");notes.Releases.RemoveAt(notes.Releases.Count-1);
bytes=NoteBytes();identity.PackageName="ResumeStudio-v0.7.6-windows-x64.zip";identity.PackageSize=200;identity.Sha256=new string('f',64);
using(var rsa=RSA.Create(2048)) {
    var metadata=JsonSerializer.SerializeToUtf8Bytes(identity,ReleaseUpdate.Json);
    var envelope=JsonSerializer.SerializeToUtf8Bytes(new {data=Convert.ToBase64String(metadata),signature=Convert.ToBase64String(rsa.SignData(metadata,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))},ReleaseUpdate.Json);
    using var handler=new NotesHandler(envelope,bytes);using var updater=new ReleaseUpdate(rsa.ExportSubjectPublicKeyInfoPem(),handler);
    Check(await updater.Check("stable","direct",current,CancellationToken.None) is not null&&updater.ChangeLog.Count==3&&handler.ApiRequests==0,"update check loads the full signed changelog without a REST API request");
    Check(await updater.Check("stable","direct",new() {Version="0.7.6"},CancellationToken.None) is null&&updater.ChangeLog.Count==1,"current-version checks retain logs without offering a duplicate update");
    handler.BadNotes=true;Check(await updater.Check("stable","direct",current,CancellationToken.None) is not null&&updater.ChangeLog.Count==0&&updater.ChangeLogError.Contains("校验失败"),"bad changelog is reported separately and never hides a verified program update");
}

string fixture=Path.Combine(Path.GetTempPath(),"ResumeMaintenance-"+Guid.NewGuid().ToString("N"));
string data=Path.Combine(fixture,"data"),install=Path.Combine(fixture,"programs","ResumeStudio");Directory.CreateDirectory(data);Directory.CreateDirectory(install);
string personal=Path.Combine(data,"personal-resume.json");await File.WriteAllTextAsync(personal,"personal data must remain");
async Task<PayloadManifest> MakePayload(string root) {
    Directory.CreateDirectory(Path.Combine(root,"Assets"));
    await File.WriteAllTextAsync(Path.Combine(root,"ResumeStudio.exe"),"owned program");await File.WriteAllTextAsync(Path.Combine(root,"sample.dll"),"owned runtime");
    await File.WriteAllTextAsync(Path.Combine(root,"Assets","ReleaseIdentity.json"),JsonSerializer.Serialize(identity,ReleaseUpdate.Json));
    var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(path=>new PayloadFile(Path.GetRelativePath(root,path),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant())).ToList();
    var manifest=new PayloadManifest {Files=files};await File.WriteAllTextAsync(Path.Combine(root,"distribution-manifest.json"),JsonSerializer.Serialize(manifest,ReleaseUpdate.Json));return manifest;
}
async Task<(string Workspace,string Payload,string Backup,string Zip)> Job(int daysAgo,bool unknownZip=false) {
    string workspace=Path.Combine(data,"updates",Guid.NewGuid().ToString("N")),payload=Path.Combine(workspace,"extracted","ResumeStudio"),backup=Path.Combine(fixture,"programs",".resumestudio-backup-"+Guid.NewGuid().ToString("N"));
    await MakePayload(payload);await MakePayload(backup);
    string zip=Path.Combine(workspace,"ResumeStudio-v0.7.6-windows-x64.zip");ZipFile.CreateFromDirectory(Path.Combine(workspace,"extracted"),zip);
    if(unknownZip)using(var archive=ZipFile.Open(zip,ZipArchiveMode.Update)){using var entry=archive.CreateEntry("personal.txt").Open();entry.Write(Encoding.UTF8.GetBytes("unknown archive data"));}
    await File.WriteAllTextAsync(Path.Combine(workspace,"request.json"),JsonSerializer.Serialize(new InstallRequest(99999999,install,payload,workspace,data),ReleaseUpdate.Json));
    await File.WriteAllTextAsync(Path.Combine(workspace,"result.json"),JsonSerializer.Serialize(new {status="installed",backup,installedAt=DateTimeOffset.Now.AddDays(-daysAgo)},ReleaseUpdate.Json));
    await File.WriteAllTextAsync(Path.Combine(workspace,"startup-ready.json"),"{}");return (workspace,payload,backup,zip);
}
var oldest=await Job(3);var latest=await Job(0);var changedArchive=await Job(2,true);
await File.WriteAllTextAsync(Path.Combine(oldest.Payload,"private.txt"),"unknown extracted data");
await File.WriteAllTextAsync(Path.Combine(oldest.Backup,"private.txt"),"unknown backup data");await File.WriteAllTextAsync(Path.Combine(oldest.Backup,"sample.dll"),"user-modified runtime");
string active=Path.Combine(data,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(active);await File.WriteAllTextAsync(Path.Combine(active,"partial.zip"),"active download");
var foreign=await Job(1);await File.WriteAllTextAsync(Path.Combine(foreign.Workspace,"request.json"),JsonSerializer.Serialize(new InstallRequest(99999999,Path.Combine(fixture,"other-app"),foreign.Payload,foreign.Workspace,data),ReleaseUpdate.Json));
var pendingStartup=await Job(1);File.Delete(Path.Combine(pendingStartup.Workspace,"startup-ready.json"));
var report=await StorageMaintenance.Cleanup(data,install);
Check(report.Workspaces==3&&!File.Exists(oldest.Zip)&&!File.Exists(latest.Zip),"completed update ZIPs are removed only after original archive contents verify");
Check(File.Exists(changedArchive.Zip),"ZIPs containing extra personal files are preserved");
Check(!File.Exists(Path.Combine(oldest.Payload,"ResumeStudio.exe"))&&File.Exists(Path.Combine(oldest.Payload,"private.txt")),"cache cleanup removes owned extracted files and preserves unknown files");
Check(File.Exists(Path.Combine(latest.Backup,"ResumeStudio.exe")),"latest successful rollback backup remains complete");
Check(!File.Exists(Path.Combine(oldest.Backup,"ResumeStudio.exe"))&&await File.ReadAllTextAsync(Path.Combine(oldest.Backup,"sample.dll"))=="user-modified runtime"&&File.Exists(Path.Combine(oldest.Backup,"private.txt")),"retiring old backups preserves changed and unknown files");
Check(await File.ReadAllTextAsync(personal)=="personal data must remain"&&File.Exists(Path.Combine(active,"partial.zip")),"personal records and active or unclassified workspaces remain untouched");
Check(File.Exists(foreign.Zip)&&File.Exists(pendingStartup.Zip)&&File.Exists(Path.Combine(foreign.Backup,"ResumeStudio.exe")),"foreign installations and unconfirmed startups cannot enter cleanup");
var again=await StorageMaintenance.Cleanup(data,install);Check(again.ReclaimedBytes==0,"repeat maintenance does not delete preserved files");

string compression=Path.Combine(fixture,"compression");await MakePayload(compression);Directory.CreateDirectory(Path.Combine(compression,"updater"));
File.Copy(args[0],Path.Combine(compression,"updater","Compress-App.ps1"));await File.WriteAllTextAsync(Path.Combine(compression,"large.dll"),new string('x',2_000_000));
var compressionManifest=new PayloadManifest {Files=Directory.GetFiles(compression,"*",SearchOption.AllDirectories).Where(path=>Path.GetFileName(path)!="distribution-manifest.json").Select(path=>new PayloadFile(Path.GetRelativePath(compression,path),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant())).ToList()};
await File.WriteAllTextAsync(Path.Combine(compression,"distribution-manifest.json"),JsonSerializer.Serialize(compressionManifest,ReleaseUpdate.Json));
await File.WriteAllTextAsync(Path.Combine(compression,"personal-extra.txt"),"do not compress unrelated files");
System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Out));
Console.WriteLine("Compression fixture: "+compression);
Check(await StorageMaintenance.Compress(compression),"verified compression helper completes successfully");
Check(compressionManifest.Files.All(file=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(compression,file.Path)))).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)),"LZX compression preserves every application file hash");
var sizes=StorageMaintenance.DirectoryBytes(compression);Console.WriteLine($"Compression measurement: {sizes.Disk}/{sizes.Logical}");Check(sizes.Disk<sizes.Logical/2,"physical disk measurement reflects executable compression");
Console.WriteLine($"PASS: {checks} maintenance checks; fixture: {fixture}; compressed bytes {sizes.Disk}/{sizes.Logical}");

sealed class NotesHandler(byte[] envelope,byte[] notes):HttpMessageHandler
{
    public int ApiRequests {get;private set;}
    public bool BadNotes {get;set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(request.RequestUri!.Host=="api.github.com"){ApiRequests++;return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));}
        byte[]? content=request.RequestUri.AbsolutePath.EndsWith("/stable.json")?envelope:request.RequestUri.AbsolutePath.EndsWith("/release-changelog.json")?(BadNotes?Encoding.UTF8.GetBytes("tampered"):notes):null;
        return Task.FromResult(content is null?new HttpResponseMessage(System.Net.HttpStatusCode.NotFound):new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(content)});
    }
}
