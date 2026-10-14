using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using System.Text;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task VerifyDirectCapture(List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        foreach(string provider in new[] {"boss","nowcoder"}) {
            string name=provider=="boss"?"BOSS直聘":"牛客",host=provider=="boss"?"https://www.zhipin.com":"https://www.nowcoder.com";
            string Link(string id)=>host+(provider=="boss"?"/job_detail/"+id+".html":"/jobs/detail/"+id);
            string a=provider+"-current-a",b=provider+"-current-b",c=provider+"-current-c";
            var original=new Job {Id=provider+"-manual",Title="手写标题",Url=Link(a)+"?channel=manual",Notes="手写备注",InfoStatus="用户核对"};await api.Save("favorites",original);
            int initial=(await api.Get<List<Job>>("favorites")).Count;
            CoreWebView2? core=null;
            Action<CoreWebView2> setup=browser=>{
                core=browser;browser.AddWebResourceRequestedFilter(host+"/*",CoreWebView2WebResourceContext.All);
                browser.WebResourceRequested+=(_,args)=>{
                    string uri=args.Request.Uri;bool drawer=uri.Contains("drawer"),incomplete=uri.Contains("incomplete"),challenge=uri.Contains("challenge");
                    string title=challenge?"安全验证":"职位夹具",id=drawer?b:uri.Contains(c)?c:a;
                    string desc=incomplete?"加载中":"岗位职责：负责嵌入式软件开发、驱动调试和设备通信，实现可靠的 C / Linux 产品功能。";
                    string detail=$"<div class='job-detail-box' data-job-id='{id}'><div class='job-banner'><div class='name'><h1>嵌入式工程师 {id}</h1></div><span class='salary'>15-25K</span></div><div class='company-name'>夹具科技</div><div class='job-area'>深圳</div><div class='job-sec-text'>{desc}</div><div class='job-requirements'>任职要求：熟悉 C 语言、Linux 与 RTOS，能够独立分析问题。</div></div>";
                    if(incomplete)detail=detail.Replace("<div class='job-requirements'>任职要求：熟悉 C 语言、Linux 与 RTOS，能够独立分析问题。</div>","");
                    bool opened=drawer||uri.Contains("/job_detail/")||uri.Contains("/jobs/detail/");
                    string recommendations=$"<ul class='recommend-list'><li class='job-card-wrapper'><a href='{Link(c)}'><h3>不应收藏的推荐职位</h3></a><div class='company-name'>推荐公司</div><div class='job-sec-text'>不应混入当前职位的推荐正文和任职要求，此职位未由用户指定。</div></li></ul>";
                    string html=$"<!DOCTYPE html><title>{title}</title>"+(opened?detail:"")+recommendations;
                    args.Response=browser.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)).AsRandomAccessStream(),200,"OK","Content-Type: text/html; charset=utf-8");
                };
            };
            async Task Loaded(string url){core!.Navigate(url);for(int i=0;i<100;i++){if(core.Source==url&&await core.ExecuteScriptAsync("document.readyState === 'complete'")=="true")break;await Task.Delay(50);}await Task.Delay(250);}
            async Task Read(ContentDialog dialog){await FeatureClick(dialog,"收藏当前职位");for(int i=0;i<100&&!dialog.IsPrimaryButtonEnabled;i++)await Task.Delay(50);await Task.Delay(100);}
            var flow=OpenPlatformCaptureAsync(provider,verificationSetup:setup);var dialog=await FeatureDialog(name+" · 收藏当前职位");
            for(int i=0;i<100;i++){if(core is not null&&await core.ExecuteScriptAsync("document.title === '职位夹具'")=="true")break;await Task.Delay(50);}
            Check(InteractionElement<ToggleSwitch>(dialog,_=>true) is null,provider+": no automatic bulk collection control");
            await Read(dialog);Check((await api.Get<List<Job>>("favorites")).Count==initial,provider+": list-only page cannot be collected");
            await Loaded(Link(a));await Task.Delay(3200);Check((await api.Get<List<Job>>("favorites")).Count==initial,provider+": opening and browsing a detail never saves automatically");
            await Read(dialog);var saved=await api.Get<List<Job>>("favorites");var enriched=saved.Single(j=>j.Id==original.Id);
            Check(saved.Count==initial&&enriched.Description.Contains("岗位职责")&&enriched.Description.Contains("任职要求")&&enriched.Company=="夹具科技"&&enriched.Location=="深圳"&&enriched.Salary=="15-25K",provider+": explicit current detail enriches missing stored fields with complete duties and requirements");
            Check(enriched.Title==original.Title&&enriched.Notes==original.Notes&&enriched.InfoStatus==original.InfoStatus,provider+": manual title, notes and verification state survive enrichment");
            Check(!enriched.Description.Contains("不应混入")&&!saved.Any(j=>j.Url.StartsWith(Link(c))),provider+": recommendations never become favorites or enter the current description");
            await Read(dialog);Check((await api.Get<List<Job>>("favorites")).Count==initial,provider+": repeated clicks do not duplicate a favorite");
            await Loaded(host+(provider=="boss"?"/web/geek/jobs?drawer=1":"/jobs/intern/center?drawer=1"));await Read(dialog);
            Check((await api.Get<List<Job>>("favorites")).Any(j=>j.Url==Link(b)&&j.Description.Contains("任职要求")),provider+": only the explicitly opened visible drawer is saved");
            await Loaded(Link(c)+"?incomplete=1");await Read(dialog);Check((await api.Get<List<Job>>("favorites")).Count==initial+1,provider+": loading placeholders are not saved as empty summaries");
            await Loaded(Link(c)+"?challenge=1");await Read(dialog);Check((await api.Get<List<Job>>("favorites")).Count==initial+1,provider+": challenge page never saves a job");
            await FeatureClick(dialog,"清空记录");Check((await api.Get<List<Job>>("favorites")).Count==initial+1,provider+": clear display preserves stored favorites");
            await FeatureClick(dialog,"结束并查看收藏");await flow.WaitAsync(TimeSpan.FromSeconds(15));await Task.Delay(200);
            Check(currentPage=="favorites"&&favoriteSource==name,provider+": finish opens the corresponding favorite filter");
            core=null;var returnFlow=OpenPlatformCaptureAsync(provider,verificationSetup:setup);var returnDialog=await FeatureDialog(name+" · 收藏当前职位");
            for(int i=0;i<100&&core is null;i++)await Task.Delay(50);await Loaded(Link(c));await Read(returnDialog);
            await FeatureClick(returnDialog,"返回");await returnFlow;await Task.Delay(200);
            Check(currentPage=="favorites"&&visibleJobList!.Items.OfType<Job>().Any(j=>j.Url==Link(c)),provider+": return refreshes newly collected current detail");
        }
    }
}
