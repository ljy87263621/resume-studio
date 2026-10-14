using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ResumeStudio.Services;

public sealed record PayloadFile(string Path,string Sha256);
public sealed class PayloadManifest {public List<PayloadFile> Files {get;set;}=[];}
public sealed record InstallRequest(int ProcessId,string InstallRoot,string SourceRoot,string Workspace,string DataDirectory,bool Restart=true);
public static class UpdateInstaller
{
    public static string ChildPath(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains(':')||Path.IsPathRooted(relative)) throw new IOException("更新文件路径无效。");
        string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("更新文件超出应用目录。");
        return full;
    }
    public static async Task<string> Prepare(string zip,string workspace,ReleaseIdentity expected,CancellationToken token)
    {
        string extracted=Path.Combine(workspace,"extracted"); Directory.CreateDirectory(extracted); long total=0;
        using(var archive=ZipFile.OpenRead(zip)) {
            if(archive.Entries.Count>5000) throw new IOException("更新文件数量异常。");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in archive.Entries) {
                token.ThrowIfCancellationRequested(); string path=ChildPath(extracted,entry.FullName);
                if(!names.Add(path)||entry.Length>128_000_000||(total+=entry.Length)>1_000_000_000||((entry.ExternalAttributes>>16)&0xf000)==0xa000) throw new IOException("更新包包含重复、链接或过大的文件。");
                if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')) {Directory.CreateDirectory(path); continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path,false);
            }
        }
        string root=Path.Combine(extracted,"ResumeStudio");
        var manifest=JsonSerializer.Deserialize<PayloadManifest>(await File.ReadAllTextAsync(Path.Combine(root,"distribution-manifest.json"),token),ReleaseUpdate.Json)??throw new IOException("更新分发清单无效。");
        var owned=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {Path.GetFullPath(Path.Combine(root,"distribution-manifest.json"))};
        foreach(var file in manifest.Files) {
            string path=ChildPath(root,file.Path); if(!owned.Add(path)) throw new IOException("分发清单存在重复路径。");
            await using var input=File.OpenRead(path); string actual=Convert.ToHexString(await SHA256.HashDataAsync(input,token)).ToLowerInvariant(); if(actual!=file.Sha256) throw new IOException("更新文件校验失败："+file.Path);
        }
        foreach(string file in Directory.EnumerateFiles(extracted,"*",SearchOption.AllDirectories)) if(!owned.Contains(Path.GetFullPath(file))) throw new IOException("更新包存在分发清单之外的文件。");
        foreach(string file in new[] {"ResumeStudio.exe","ResumeStudio.dll","Assets/AppIcon.ico","Assets/ReleaseIdentity.json","runtime/bin/java.exe","backend/resume-backend.jar"}) if(!File.Exists(ChildPath(root,file))) throw new IOException("更新包缺少必需文件："+file);
        var identity=ReleaseIdentity.Current(root);
        if(identity.Channel!=expected.Channel||identity.Version!=expected.Version||identity.Commit!=expected.Commit||identity.Tag!=expected.Tag) throw new IOException("安装包构建身份与签名不一致。");
        return root;
    }
    public static async Task Launch(string source,string workspace,string installRoot,string dataDirectory)
    {
        // The verified incoming payload carries its own updater. Using the installed
        // helper would make a failed upgrade impossible to repair from the new build.
        foreach(string file in new[] {"Update-App.ps1","sync-distribution.ps1"}) File.Copy(Path.Combine(source,"updater",file),Path.Combine(workspace,file),true);
        string request=Path.Combine(workspace,"request.json");
        await File.WriteAllTextAsync(request,JsonSerializer.Serialize(new InstallRequest(Environment.ProcessId,Path.GetFullPath(installRoot),source,workspace,dataDirectory),ReleaseUpdate.Json));
        // A desktop shortcut starts the app inside its installation directory. The
        // updater must run elsewhere so its own current directory cannot block the swap.
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=workspace};
        foreach(string arg in new[] {"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(workspace,"Update-App.ps1"),"-RequestFile",request}) info.ArgumentList.Add(arg);
        _=Process.Start(info)??throw new IOException("更新安装器未能启动。");
    }
}
