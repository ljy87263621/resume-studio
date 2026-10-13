using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResumeStudio.Services;

public sealed class StorageMaintenanceResult
{
    public long ReclaimedBytes {get;set;}
    public int Workspaces {get;set;}
    public int RetiredBackups {get;set;}
    public int PreservedFiles {get;set;}
    public string KeptBackup {get;set;}="";
}

/// <summary>Retire verified update payloads; personal, modified and unclassified files survive.</summary>
public static class StorageMaintenance
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetCompressedFileSize(string name,out uint high);
    public static long DiskBytes(string path)
    {
        uint low=GetCompressedFileSize(path,out uint high);
        return low==uint.MaxValue&&Marshal.GetLastWin32Error()!=0?new FileInfo(path).Length:(long)(((ulong)high<<32)|low);
    }
    public static (long Logical,long Disk) DirectoryBytes(string root)
    {
        long logical=0,disk=0;if(!Directory.Exists(root))return (0,0);
        var pending=new Stack<string>();pending.Push(root);
        while(pending.Count>0) {
            var directory=pending.Pop();
            try {
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(var path in Directory.EnumerateFileSystemEntries(directory)) {
                    var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)continue;
                    if((attributes&FileAttributes.Directory)!=0)pending.Push(path);
                    else {logical+=new FileInfo(path).Length;disk+=DiskBytes(path);}
                }
            } catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
        }return (logical,disk);
    }
    private static string Full(string path)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    private static bool Same(string left,string right)=>string.Equals(Full(left),Full(right),StringComparison.OrdinalIgnoreCase);
    private static bool HasLinks(string root)
    {
        if(!Directory.Exists(root))return false;
        var pending=new Stack<string>();pending.Push(root);
        while(pending.Count>0) {
            var directory=pending.Pop();
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return true;
            foreach(var entry in Directory.EnumerateFileSystemEntries(directory)) {
                var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)return true;
                if((attributes&FileAttributes.Directory)!=0)pending.Push(entry);
            }
        }return false;
    }
    private static async Task<T?> Json<T>(string path,CancellationToken token)
    {
        if(!File.Exists(path)||new FileInfo(path).Length>2_000_000)return default;
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path,token),ReleaseUpdate.Json);
    }
    private static async Task<PayloadManifest?> Manifest(string root,CancellationToken token)
    {
        if(!Directory.Exists(root)||HasLinks(root))return null;
        var manifest=await Json<PayloadManifest>(Path.Combine(root,"distribution-manifest.json"),token);
        if(manifest?.Files is null||manifest.Files.Count==0||manifest.Files.Count>5000)return null;
        var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files) {
            if(file is null||string.IsNullOrWhiteSpace(file.Path)||!Regex.IsMatch(file.Sha256??"",@"^[0-9a-fA-F]{64}$"))return null;
            if(!unique.Add(UpdateInstaller.ChildPath(root,file.Path)))return null;
        }return manifest;
    }
    private static async Task<bool> Matches(string path,string hash,CancellationToken token)
    {
        await using var stream=File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).Equals(hash,StringComparison.OrdinalIgnoreCase);
    }
    private static void DeleteFile(string path,StorageMaintenanceResult report)
    {
        long bytes=DiskBytes(path);File.Delete(path);report.ReclaimedBytes+=bytes;
    }
    private static async Task CleanPayload(string root,PayloadManifest manifest,StorageMaintenanceResult report,CancellationToken token)
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files) {
            token.ThrowIfCancellationRequested();string path=UpdateInstaller.ChildPath(root,file.Path);paths.Add(path);
            if(!File.Exists(path))continue;
            try {if(await Matches(path,file.Sha256,token))DeleteFile(path,report);else report.PreservedFiles++;}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){report.PreservedFiles++;}
        }
        string metadata=Path.Combine(root,"distribution-manifest.json");
        var remaining=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).ToList();
        report.PreservedFiles+=remaining.Count(path=>!Same(path,metadata)&&!paths.Contains(path));
        if(remaining.Count==1&&Same(remaining[0],metadata))DeleteFile(metadata,report);
        // Never recursively delete. Unknown contents, changed files and new links remain.
        foreach(string directory in Directory.EnumerateDirectories(root,"*",SearchOption.AllDirectories).OrderByDescending(path=>path.Length))
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)==0&&!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory,false);
        if(!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root,false);
    }
    private static async Task<bool> LegacyArchiveMatches(string archive,PayloadManifest manifest,CancellationToken token)
    {
        // Older updater requests lack the ZIP hash. Verify every archive file against
        // its original manifest before deleting it; added personal files prevent deletion.
        using var zip=ZipFile.OpenRead(archive);if(zip.Entries.Count>5000)return false;
        var owned=manifest.Files.ToDictionary(file=>"ResumeStudio/"+file.Path.Replace('\\','/'),file=>file.Sha256,StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total=0;
        foreach(var entry in zip.Entries) {
            string name=entry.FullName.Replace('\\','/');if(name.EndsWith('/'))continue;
            if(!seen.Add(name)||entry.Length>128_000_000||(total+=entry.Length)>1_000_000_000)return false;
            if(name=="ResumeStudio/distribution-manifest.json") {
                if(entry.Length>2_000_000)return false;
                using var stream=entry.Open();var original=await JsonSerializer.DeserializeAsync<PayloadManifest>(stream,ReleaseUpdate.Json,token);
                if(original?.Files is null||original.Files.Count!=manifest.Files.Count||original.Files.Any(file=>!owned.TryGetValue("ResumeStudio/"+file.Path.Replace('\\','/'),out var hash)||!hash.Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)))return false;
            } else {
                if(!owned.TryGetValue(name,out var hash))return false;
                using var stream=entry.Open();if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).Equals(hash,StringComparison.OrdinalIgnoreCase))return false;
            }
        }return seen.Count==owned.Count+1;
    }
    public static async Task<StorageMaintenanceResult> Cleanup(string dataDirectory,string installDirectory,CancellationToken token=default)
    {
        string data=Full(dataDirectory),install=Full(installDirectory),updates=Path.Combine(data,"updates");var report=new StorageMaintenanceResult();
        if(!Directory.Exists(updates)||(File.GetAttributes(updates)&FileAttributes.ReparsePoint)!=0)return report;
        var backups=new List<(string Root,DateTimeOffset Completed)>();
        foreach(string workspace in Directory.GetDirectories(updates)) {
            token.ThrowIfCancellationRequested();if(!Regex.IsMatch(Path.GetFileName(workspace),@"^[0-9a-f]{32}$"))continue;
            try {
                if(HasLinks(workspace))continue;
                var request=await Json<InstallRequest>(Path.Combine(workspace,"request.json"),token);
                using var result=await Json<JsonDocument>(Path.Combine(workspace,"result.json"),token);
                if(request is null||result is null||!Same(request.DataDirectory,data)||!Same(request.InstallRoot,install)||!Same(request.Workspace,workspace)||!Same(request.SourceRoot,Path.Combine(workspace,"extracted","ResumeStudio")))continue;
                if(!result.RootElement.TryGetProperty("status",out var state)||state.GetString() is not ("installed" or "failed"))continue;
                if(state.GetString()=="failed"&&DateTime.UtcNow-File.GetLastWriteTimeUtc(Path.Combine(workspace,"result.json"))<TimeSpan.FromHours(1))continue;
                if(state.GetString()=="installed"&&request.Restart&&!File.Exists(Path.Combine(workspace,"startup-ready.json")))continue;
                var manifest=await Manifest(request.SourceRoot,token);
                if(manifest is not null) {
                    var identity=ReleaseIdentity.Current(request.SourceRoot);
                    string package=PackageName(identity);
                    if(package!="") {
                        string archive=Path.Combine(workspace,package);
                        if(File.Exists(archive)&&await LegacyArchiveMatches(archive,manifest,token))DeleteFile(archive,report);
                    }
                    await CleanPayload(request.SourceRoot,manifest,report,token);report.Workspaces++;
                }
                if(state.GetString()=="installed"&&result.RootElement.TryGetProperty("backup",out var backupValue)) {
                    string backup=Full(backupValue.GetString()??"");
                    if(Same(Path.GetDirectoryName(backup)!,Path.GetDirectoryName(install)!)&&Regex.IsMatch(Path.GetFileName(backup),@"^\.resumestudio-backup-[0-9a-f]{32}$")) {
                        var completed=result.RootElement.TryGetProperty("installedAt",out var at)&&DateTimeOffset.TryParse(at.GetString(),out var time)?time:new DateTimeOffset(File.GetLastWriteTimeUtc(Path.Combine(workspace,"result.json")));
                        backups.Add((backup,completed));
                    }
                }
            } catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { }
        }
        // The latest successful rollback directory remains intact. Older ones lose
        // only their unchanged, manifest-owned payload; unknown and edited files survive.
        var existingBackups=backups.DistinctBy(item=>item.Root,StringComparer.OrdinalIgnoreCase).Where(item=>Directory.Exists(item.Root)).OrderByDescending(item=>item.Completed).ToList();
        report.KeptBackup=existingBackups.FirstOrDefault().Root??"";
        foreach(var backup in existingBackups.Skip(1)) {
            try {
                var manifest=await Manifest(backup.Root,token);
                if(manifest is not null){await CleanPayload(backup.Root,manifest,report,token);report.RetiredBackups++;}
            } catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }return report;
    }
    private static string PackageName(ReleaseIdentity identity)
    {
        if(!Regex.IsMatch(identity.Version,@"^\d+\.\d+\.\d+$"))return "";
        if(identity.Channel=="stable"&&identity.Tag=="v"+identity.Version)return $"ResumeStudio-v{identity.Version}-windows-x64.zip";
        if(identity.Channel=="nightly"&&Regex.IsMatch(identity.Commit,@"^[0-9a-f]{40}$")&&Regex.IsMatch(identity.Tag,@"^nightly-\d{8}-[0-9a-f]{8}$")&&identity.Tag.EndsWith(identity.Commit[..8]))
            return $"ResumeStudio-v{identity.Version}-nightly.{identity.Tag.Split('-')[1]}.{identity.Commit[..8]}-windows-x64.zip";
        return "";
    }
    public static async Task<bool> Compress(string installDirectory,CancellationToken token=default,string? backupDirectory=null)
    {
        string install=Full(installDirectory);if(HasLinks(install))return false;
        var manifest=await Manifest(install,token);if(manifest is null)return false;
        var helper=manifest.Files.SingleOrDefault(file=>file.Path.Replace('\\','/')=="updater/Compress-App.ps1");
        if(helper is null)return false;string script=UpdateInstaller.ChildPath(install,helper.Path);
        if(!await Matches(script,helper.Sha256,token))return false;
        string target=backupDirectory is null?install:Full(backupDirectory);
        if(!Same(target,install)&&(!Same(Path.GetDirectoryName(target)!,Path.GetDirectoryName(install)!)||!Regex.IsMatch(Path.GetFileName(target),@"^\.resumestudio-backup-[0-9a-f]{32}$")))return false;
        if(await Manifest(target,token) is null)return false;
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetTempPath(),RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment.Remove("PSModulePath");
        foreach(string argument in new[] {"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-AppDirectory",target})info.ArgumentList.Add(argument);
        using var process=Process.Start(info);if(process is null)return false;
        var output=process.StandardOutput.ReadToEndAsync(token);var error=process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);await output;var diagnostics=await error;
        if(process.ExitCode!=0)System.Diagnostics.Trace.WriteLine("App compression failed: "+diagnostics);
        return process.ExitCode==0;
    }
}
