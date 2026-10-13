using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task VerifyEmployerCapture(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        var browser=new WebView2 {Width=600,Height=400};body.Children.Add(browser);
        try {
            webEnvironment??=CreateWebEnvironment();await browser.EnsureCoreWebView2Async(await webEnvironment);
            var loaded=new TaskCompletionSource<bool>();browser.CoreWebView2.NavigationCompleted+=(_,e)=>loaded.TrySetResult(e.IsSuccess);
            browser.NavigateToString("<title>测试工程师-示例科技</title><header>产品中心</header><h1>测试工程师</h1><main><div class='entry-content'><h3><strong>岗位职责：</strong></h3><p>开发固件并验证数据通信。</p><h3>任职资格：</h3><p>掌握 C 和 Linux。</p><h3>工作地点：</h3><p>广州</p><div hidden>内部隐藏数据</div></div></main><footer>产品中心 版权所有</footer>");
            if(!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(12)))throw new Exception("Employer browser fixture did not load");
            using var data=JsonDocument.Parse(await browser.CoreWebView2.ExecuteScriptAsync(ReadVisibleJobScript));var fields=data.RootElement.GetProperty("fields");
            Check(fields.GetProperty("title").GetString()=="测试工程师"&&fields.GetProperty("company").GetString()=="示例科技"&&fields.GetProperty("location").GetString()=="广州","browser reader extracts employer title company and heading-based location");
            string description=fields.GetProperty("description").GetString()!;
            Check(description.Contains("岗位职责")&&description.Contains("任职资格")&&!description.Contains("产品中心")&&!description.Contains("隐藏数据"),"browser reader captures visible employer body without navigation footer or hidden text");
        }
        finally {body.Children.Remove(browser);browser.Close();}
        if(Environment.GetEnvironmentVariable("RESUMESTUDIO_LIVE_CAPTURE")!="1")return;
        const string url="https://www.vsol.com.cn/join-us/campus-recruitment/embedded-software-engineer-1";
        var captured=await ResolveJobLink(url);
        Check(captured.Status=="parsed"&&captured.Job.Title=="嵌入式软件开发工程师（2027届）"&&captured.Job.Company=="芯德科技"&&captured.Job.Location=="广州","live user-provided VSOL URL parses through the production backend endpoint");
        Check(captured.Job.Description.Contains("Linux")&&captured.Job.Description.Contains("任职资格")&&!captured.Job.Description.Contains("产品中心")&&captured.Job.Salary=="","live employer capture keeps real duties and leaves unpublished salary empty");
        var edit=EditFavorite(captured.Job,true,captured.Message);var dialog=await FeatureDialog("核对并收藏职位");
        Check(FindTextField(dialog,"职位名称")?.Text==captured.Job.Title&&FindTextField(dialog,"城市 / 地点")?.Text=="广州","actual favorite confirmation dialog is prefilled from live employer capture");
        await FeatureClick(dialog,"保存收藏");await edit;
        var saved=(await api.Get<List<Job>>("favorites")).Single(j=>j.Id==captured.Job.Id);
        Check(saved.Url==url&&saved.Company=="芯德科技"&&saved.Description.Contains("Linux"),"native confirmation saves the original employer URL and extracted fields into isolated favorites");
        await SaveNativePreview(Path.Combine(output,"employer-favorite.png"));
    }
}
