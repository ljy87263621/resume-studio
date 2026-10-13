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
      const title=pick(['.job-banner .name h1','.job-name h1','.job-title','main h1','article h1','h1[itemprop="title"]','h1','.entry-content h2']);
      const body=pick(['.entry-content','.job-detail','main','article']);
      const labeled=(labels)=>{const m=body.match(new RegExp('(?:^|\\n)\\s*(?:'+labels+')\\s*[：:]?\\s*(?:\\n\\s*)?([^\\n]+)','i'));return m&&m[1].trim().length<=200&&!/[：:]$/.test(m[1].trim())?m[1].trim():'';};
      let company=pick(['.company-info .name','.company-info a','.company-name a','.company-name','[itemprop="hiringOrganization"]'])||labeled('招聘公司|公司名称|Hiring organization');
      if(!company){const pieces=document.title.split(/\s*[|｜—]\s*|\s*-\s*/),last=pieces[pieces.length-1].trim();if(pieces.length>1&&last.length<=120&&/(科技|公司|集团|银行|研究院|医院|大学|股份|有限公司)$/.test(last)&&!/(招聘|职位|岗位)/.test(last))company=last;}
      const salary=pick(['.job-banner .salary','.job-salary','.salary'])||labeled('薪资范围|薪资待遇|薪资|工资|Salary');
      const location=pick(['.job-area','.job-location','[itemprop="addressLocality"]'])||labeled('工作地点|工作地址|工作城市|办公地点|Location');
      let description=pick(['.job-sec-text','.job-description','.job-detail .text','[itemprop="description"]']);
      const duties=body.match(/岗位职责|工作职责|职位职责|工作内容|职位描述|职责描述|任职资格|任职要求|岗位要求|职位要求|responsibilities|requirements|qualifications/gi)||[];
      if(!description&&duties.length>=2&&!/^(人才招聘|校园招聘|社会招聘|Careers|Jobs|Join us)$/i.test(title))description=body;
      const text=body||description;
      return {url:window.location.href,text:text.slice(0,40000),fields:{title,company,salary,location,description:description.slice(0,30000)}};
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
                    if(string.IsNullOrWhiteSpace(title)||System.Text.RegularExpressions.Regex.IsMatch(title,@"(?i)安全验证|人机验证|请稍候|访问验证|Access denied|Checking your browser|Just a moment|captcha|^(?:\d{4}年?\s*)?(登录|登录注册|人才招聘|校园招聘|社会招聘|Careers|Jobs|Join us)$")||fields.Where(p=>p.Key!="title").All(p=>string.IsNullOrWhiteSpace(p.Value)))
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
