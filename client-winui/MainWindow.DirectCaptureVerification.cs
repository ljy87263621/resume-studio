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
        var original=new Job {Id="manual-existing",Title="手写职位标题",Company="测试公司",Url="https://www.nowcoder.com/jobs/detail/direct-existing?channel=manual",Notes="手写备注",InfoStatus="用户核对"};
        await api.Save("favorites",original);
        CoreWebView2? core=null;
        Action<CoreWebView2> setup=browser=>{
            core=browser;browser.AddWebResourceRequestedFilter("https://www.nowcoder.com/*",CoreWebView2WebResourceContext.All);
            browser.WebResourceRequested+=(_,args)=>{
                string extra=args.Request.Uri.Contains("second")?"<li class='job-item'><a href='/jobs/detail/direct-third'><h3>后续新职位</h3></a><div class='company-name'>测试公司</div></li>":"";
                if(args.Request.Uri.Contains("return"))extra="<li class='job-item'><a href='/jobs/detail/direct-return'><h3>返回后可见职位</h3></a><div class='company-name'>测试公司</div></li>";
                string html="<!DOCTYPE html><title>职位列表夹具</title><ul><li class='job-item'><a href='/jobs/detail/direct-first'><h3>新读取职位</h3></a><div class='company-name'>测试公司</div></li><li class='job-item'><a href='/jobs/detail/direct-existing?channel=capture'><h3>原站标题</h3></a><div class='company-name'>测试公司</div></li>"+extra+"</ul>";
                args.Response=browser.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)).AsRandomAccessStream(),200,"OK","Content-Type: text/html; charset=utf-8");
            };
        };
        var flow=OpenPlatformCaptureAsync("nowcoder",verificationSetup:setup);
        var dialog=await FeatureDialog("牛客 · 浏览并直接收藏");
        for(int attempt=0;attempt<100;attempt++){if(core is not null&&await core.ExecuteScriptAsync("document.title === '职位列表夹具'")=="true")break;await Task.Delay(50);}
        await Task.Delay(3200);
        Check(InteractionElement<ToggleSwitch>(dialog,_=>true)?.IsOn==false&&(await api.Get<List<Job>>("favorites")).Count==1,"opening capture defaults to manual mode and does not save merely by browsing");
        Check(FeatureButton(dialog,"全选") is null&&FeatureButton(dialog,"核对选中职位") is null,"direct collection has no selection or review step");
        await FeatureClick(dialog,"读取当前页并收藏");
        for(int attempt=0;attempt<100;attempt++){if((await api.Get<List<Job>>("favorites")).Count==2&&dialog.IsPrimaryButtonEnabled)break;await Task.Delay(50);}
        var saved=await api.Get<List<Job>>("favorites");Check(saved.Count==2&&saved.Any(j=>j.Title=="新读取职位"),"native read action writes the parsed job directly into actual favorite storage");
        Check(saved.Single(j=>j.Id==original.Id).Notes==original.Notes&&saved.Single(j=>j.Id==original.Id).Title==original.Title,"platform tracking aliases preserve an existing manually edited favorite");
        await FeatureClick(dialog,"读取当前页并收藏");await Task.Delay(250);Check((await api.Get<List<Job>>("favorites")).Count==2,"repeated native reading does not duplicate favorites");
        await FeatureClick(dialog,"清空列表");Check((await api.Get<List<Job>>("favorites")).Count==2,"clearing the session display never deletes saved favorites");
        InteractionElement<ToggleSwitch>(dialog,_=>true)!.IsOn=true;core!.Navigate("https://www.nowcoder.com/jobs/intern/center?second=1");
        for(int attempt=0;attempt<100;attempt++){if((await api.Get<List<Job>>("favorites")).Count==3&&dialog.IsPrimaryButtonEnabled)break;await Task.Delay(50);}
        Check((await api.Get<List<Job>>("favorites")).Any(j=>j.Title=="后续新职位"),"enabling continuous capture saves newly navigated jobs without another click");
        await FeatureClick(dialog,"结束并查看收藏");await flow.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(200);
        Check(currentPage=="favorites"&&favoriteSource=="牛客"&&FeatureButton(body,"取消收藏") is not null,"finishing capture opens favorites with the current platform filter");
        core=null;var returnFlow=OpenPlatformCaptureAsync("nowcoder",verificationSetup:setup);var returnDialog=await FeatureDialog("牛客 · 浏览并直接收藏");
        for(int attempt=0;attempt<100;attempt++){if(core is not null&&core.Source.StartsWith("https://www.nowcoder.com/"))break;await Task.Delay(50);}
        core!.Navigate("https://www.nowcoder.com/jobs/intern/center?return=1");
        for(int attempt=0;attempt<100;attempt++){if(await core.ExecuteScriptAsync("document.body.innerText.includes('返回后可见职位')")=="true")break;await Task.Delay(50);}
        await FeatureClick(returnDialog,"读取当前页并收藏");
        for(int attempt=0;attempt<100;attempt++){if((await api.Get<List<Job>>("favorites")).Count==4&&returnDialog.IsPrimaryButtonEnabled)break;await Task.Delay(50);}
        await FeatureClick(returnDialog,"返回");await returnFlow;await Task.Delay(200);
        Check(currentPage=="favorites"&&visibleJobList!.Items.OfType<Job>().Any(j=>j.Title=="返回后可见职位"),"returning to the original favorites page refreshes newly saved jobs immediately");
    }
}
