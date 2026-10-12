using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private const string ReadVisibleJobScript="""
    (()=>{
      const pick=(selectors)=>{
        for(const selector of selectors){
          for(const element of document.querySelectorAll(selector)){
            if(element.getClientRects().length && element.innerText.trim())return element.innerText.trim();
          }
        }return '';
      };
      const title=pick(['.job-banner .name h1','.job-name h1','.job-title','h1[itemprop="title"]','h1']);
      const company=pick(['.company-info .name','.company-info a','.company-name a','.company-name','[itemprop="hiringOrganization"]']);
      const salary=pick(['.job-banner .salary','.job-salary','.salary']);
      const location=pick(['.job-area','.job-location','[itemprop="addressLocality"]']);
      const description=pick(['.job-sec-text','.job-description','.job-detail .text','[itemprop="description"]']);
      const text=(document.querySelector('.job-detail')||document.querySelector('main'))?.innerText||description;
      return {url:window.location.href,text:text.slice(0,40000),fields:{title,company,salary,location,description}};
    })()
    """;
    private async Task<Job?> ReadVisibleJob(Job original)
    {
        var valid=await api.Post<CapturePreview>("capture-preview",new {text=original.Url});
        var web=new WebView2 {Width=Math.Max(320,Math.Min(940,shell.ActualWidth-120)),Height=Math.Max(280,Math.Min(530,shell.ActualHeight-220))};
        var status=Text("正常浏览职位详情；如页面要求登录或验证，请在此页面完成后再点击读取。",12);
        var content=Stack(10); content.Children.Add(status); content.Children.Add(web);
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title="浏览职位并读取",Content=content,PrimaryButtonText="读取当前职位",SecondaryButtonText="在浏览器打开",CloseButtonText="返回"};
        dialog.Resources["ContentDialogMaxWidth"]=1060d;
        bool initialized=false;
        dialog.Opened+=async (_,_)=> {
            if(initialized) return;
            try
            {
                webEnvironment??=CreateWebEnvironment(); await web.EnsureCoreWebView2Async(await webEnvironment);
                web.CoreWebView2!.Settings.AreDevToolsEnabled=false;
                web.CoreWebView2.NavigationStarting+=(_,e)=> { if(!e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&e.Uri!="about:blank") e.Cancel=true; };
                web.CoreWebView2.NewWindowRequested+=(_,e)=> { e.Handled=true; if(e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase)) web.CoreWebView2.Navigate(e.Uri); };
                initialized=true; web.CoreWebView2.Navigate(valid.Job.Url);
            }
            catch(Exception e) { status.Text="页面未能加载："+e.Message; }
        };
        try
        {
            while(true)
            {
                var choice=await dialog.ShowAsync();
                if(choice==ContentDialogResult.None) return null;
                if(choice==ContentDialogResult.Secondary) { await Launch(valid.Job.Url); continue; }
                if(web.CoreWebView2 is null) { status.Text="页面尚未加载，请稍候。"; continue; }
                try
                {
                    // Read visible DOM text only after the user requests it. No cookies, hidden APIs, or challenge automation.
                    string json=await web.CoreWebView2.ExecuteScriptAsync(ReadVisibleJobScript);
                    using var parsed=JsonDocument.Parse(json); var data=parsed.RootElement;
                    string url=data.GetProperty("url").GetString()??"",visibleText=data.GetProperty("text").GetString()??"";
                    var fields=JsonSerializer.Deserialize<Dictionary<string,string>>(data.GetProperty("fields").GetRawText(),BackendClient.Json)!;
                    string title=fields.GetValueOrDefault("title","");
                    if(string.IsNullOrWhiteSpace(title)||title.StartsWith("请稍候")||title is "安全验证" or "人机验证" or "登录" or "登录注册" or "访问验证"||fields.Where(p=>p.Key!="title").All(p=>string.IsNullOrWhiteSpace(p.Value)))
                    { status.Text="当前页面尚未显示完整职位。请正常完成访问验证、登录或返回具体职位页后再读取。"; continue; }
                    var captured=await api.Post<CapturePreview>("capture-preview",new {text=url+"\n"+visibleText,visibleFields=fields});
                    var job=JsonSerializer.Deserialize<Job>(JsonSerializer.Serialize(original,BackendClient.Json),BackendClient.Json)!;
                    job.Url=captured.Job.Url; job.CanonicalUrl=captured.Job.CanonicalUrl;
                    job.Title=captured.Job.Title;
                    if(!string.IsNullOrWhiteSpace(captured.Job.Company)) job.Company=captured.Job.Company;
                    if(!string.IsNullOrWhiteSpace(captured.Job.Location)) job.Location=captured.Job.Location;
                    if(!string.IsNullOrWhiteSpace(captured.Job.Salary)) job.Salary=captured.Job.Salary;
                    if(!string.IsNullOrWhiteSpace(captured.Job.Description)) job.Description=captured.Job.Description;
                    job.InfoStatus=captured.Job.InfoStatus; return job;
                }
                catch(Exception e) { status.Text="读取未完成："+e.Message; }
            }
        }
        finally { web.Close(); }
    }
}
