using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task LightweightTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"lightweight");Directory.CreateDirectory(output);
        var checks=new List<string>();WebView2? browser=null;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        try {
            var extension=JsonSerializer.Deserialize<Resume>("""
            {"id":"lightweight-legacy","title":"旧版资料","templateId":"gh-back-awesome","future":{"precision":123456789012345678901234567890,"Case":"keep"},"profile":{"name":"测试姓名","futureProfile":true},"sections":[{"title":"技能","content":"- C / Linux","visible":true,"futureSection":{"keep":true}}]}
            """,BackendClient.Json)!;
            var saved=await api.Save("resumes",extension);current=CloneResume(saved);dirty=false;await OpenEditor();await WaitForPreview();
            var copy=CloneDocument(current);var json=JsonSerializer.Serialize(copy,BackendClient.Json);
            Check(json.Contains("123456789012345678901234567890")&&json.Contains("futureProfile")&&json.Contains("futureSection"),"trimmed executable retains reflection-based models and exact unknown data");
            FindTextField(body,"姓名")!.Text="轻量版保存验证";await Task.Delay(80);await SaveCurrent();
            var reread=(await api.Get<List<Resume>>("resumes")).Single(r=>r.Id==current.Id);json=JsonSerializer.Serialize(reread,BackendClient.Json);
            Check(reread.Profile.Name=="轻量版保存验证"&&json.Contains("123456789012345678901234567890"),"native edit and Java persistence retain legacy extensions");
            await PrintPdf(Path.Combine(output,"lightweight.pdf"));var pdf=await PdfInspection.Read(Path.Combine(output,"lightweight.pdf"));
            Check(pdf.Count>0&&pdf.All(p=>!p.NearlyBlank),"trimmed Windows projection supports PDF export and native PDF rendering");
            var open=new Windows.Storage.Pickers.FileOpenPicker();open.FileTypeFilter.Add(".json");InitPicker(open);
            var save=new Windows.Storage.Pickers.FileSavePicker();save.FileTypeChoices.Add("备份",new List<string> {".json"});InitPicker(save);
            Check(open.FileTypeFilter.Contains(".json")&&save.FileTypeChoices.ContainsKey("备份"),"Windows import/export picker activation and COM initialization remain available");
            await Navigate("settings");await Task.Delay(160);Check(FeatureButton(body,"检查并同步") is not null,"WebDAV and update settings remain available in the trimmed client");
            browser=new WebView2 {Width=1,Height=1};body.Children.Add(browser);webEnvironment??=CreateWebEnvironment();await browser.EnsureCoreWebView2Async(await webEnvironment);
            var core=browser.CoreWebView2!;
            var cookie=core.CookieManager.CreateCookie("retained-login-fixture","keep-login","cache-fixture.invalid","/");core.CookieManager.AddOrUpdateCookie(cookie);
            Check(await MaintainBrowserDiskCache(core,true),"browser cache maintenance uses the public disk-cache clearing API");
            Check((await core.CookieManager.GetCookiesAsync("https://cache-fixture.invalid/")).Any(c=>c.Name==cookie.Name&&c.Value==cookie.Value),"disk-cache maintenance preserves browser login cookies");
            var usage=StorageMaintenance.DirectoryBytes(AppContext.BaseDirectory);
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {status="passed",checks,program=new {logical=usage.Logical,disk=usage.Disk}},BackendClient.Json));
        }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {status="failed",checks,error=ex.ToString()},BackendClient.Json));}
        finally {if(browser is not null){body.Children.Remove(browser);browser.Close();}await SaveCurrent();await DetachPreviewAsync(true);closing=true;api.Dispose();Close();}
    }
}
