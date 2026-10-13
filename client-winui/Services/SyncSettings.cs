using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace ResumeStudio.Services;

/// <summary>Connection settings stay local. Password is protected for the current Windows user.</summary>
public sealed class SyncSettings
{
    public string DirectoryUrl { get; set; }="";
    public string Username { get; set; }="";
    public string ProtectedPassword { get; set; }="";
    public string DeviceId { get; set; }=Guid.NewGuid().ToString("N");
    private static string PathFor(string directory)=>Path.Combine(directory,"sync-local.json");
    public static SyncSettings Load(string directory)
    {
        string path=PathFor(directory); if(!File.Exists(path)) return new();
        try
        {
            if(new FileInfo(path).Length>64*1024) throw new IOException("本机同步设置过大。");
            return JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(path),BackendClient.Json)??throw new IOException("本机同步设置为空。");
        }
        catch(JsonException ex) { throw new IOException("本机同步设置无效，请删除 sync-local.json 后重新配置。",ex); }
    }
    public async Task<string> PasswordAsync()
    {
        if(ProtectedPassword=="") return "";
        try
        {
            var bytes=Convert.FromBase64String(ProtectedPassword);
            var plain=await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.CreateFromByteArray(bytes));
            return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8,plain);
        }
        catch(Exception ex) { throw new IOException("无法解密 WebDAV 密码，请在本机重新填写并保存。",ex); }
    }
    public async Task SaveAsync(string directory,string? newPassword)
    {
        DirectoryUrl=SyncWebDav.Address(DirectoryUrl).AbsoluteUri;
        if(Username.Contains(':')||Username.Length>1024) throw new IOException("WebDAV 用户名无效。");
        if(newPassword is not null)
        {
            var buffer=await new DataProtectionProvider("LOCAL=user").ProtectAsync(CryptographicBuffer.ConvertStringToBinary(newPassword,BinaryStringEncoding.Utf8));
            CryptographicBuffer.CopyToByteArray(buffer,out var bytes); ProtectedPassword=Convert.ToBase64String(bytes);
        }
        Directory.CreateDirectory(directory); string path=PathFor(directory);
        await File.WriteAllTextAsync(path+".tmp",JsonSerializer.Serialize(this,BackendClient.Json)); File.Move(path+".tmp",path,true);
    }
    public string Target=>SyncProtocol.Hash(new System.Text.Json.Nodes.JsonObject { ["url"]=DirectoryUrl,["user"]=Username });
}
