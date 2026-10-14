using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    // Only job fields cross the script boundary; browser login state and cookies stay in WebView2.
    private const string PlatformJobsScript="""
    (()=>{
      const boss=location.hostname==='www.zhipin.com'||location.hostname.endsWith('.zhipin.com');
      const detail=(url)=>{try{const u=new URL(url,location.href);return u.protocol==='https:'&&(boss?/(^|\.)zhipin\.com$/.test(u.hostname)&&/^\/job_detail\/[\w-]+\.html$/.test(u.pathname):/(^|\.)nowcoder\.com$/.test(u.hostname)&&/^\/jobs\/detail\/[\w-]+\/?$/.test(u.pathname));}catch{return false;}};
      const text=(root,selectors)=>{for(const selector of selectors){const e=root.querySelector(selector);if(e&&e.getClientRects().length&&e.innerText.trim())return e.innerText.trim();}return '';};
      const jobs=[]; const seen=new Set();
      const add=(j)=>{if(j.url&&j.title&&detail(j.url)&&!seen.has(j.url)&&jobs.length<500){seen.add(j.url);j.description=(j.description||'').slice(0,30000);jobs.push(j);}};
      for(const a of document.querySelectorAll(boss?'a[href*="/job_detail/"]':'a[href*="/jobs/detail/"]')){
        if(!a.getClientRects().length||!detail(a.href))continue;
        const card=a.closest('.job-card-wrapper,.job-card-box,.job-card-item,.job-item,li')||a.parentElement;
        add({url:a.href,title:text(card,['.job-name','.job-title','h3','h2'])||a.innerText.trim().split('\n')[0],company:text(card,['.company-name','.company-title','.company-info .name']),salary:text(card,['.salary','.job-salary','.salary-text']),location:text(card,['.job-area','.job-location','.job-info-item:nth-child(2)']),description:''});
      }
      // Initial state often supplies server-rendered Nowcoder jobs before any JSON response is emitted.
      // Walk bounded JSON objects, selecting the same job fields as the native extractor.
      let visited=0;
      const visit=(value,depth)=>{
        if(!value||typeof value!=='object'||depth>24||++visited>20000||jobs.length>=500)return;
        if(!Array.isArray(value)){
          const p=value.jobInfo||value,title=p.jobName||p.jobTitle||p.positionName;
          const id=boss?p.encryptJobId:(p.jobId||p.id);
          if(title&&id&&(boss||p.recruitType||p.companyId||p.recommendInternCompany)){
            let description=p.postDescription||p.jobDescription||p.description||p.jobDesc||'';
            if(!description&&typeof p.ext==='string'){try{const ext=JSON.parse(p.ext);description=[ext.infos,ext.requirements].filter(Boolean).join('\n');}catch{}}
            const company=p.brandName||p.companyName||p.companyShortName||(p.recommendInternCompany||{}).companyName||(value.brandComInfo||{}).brandName||'';
            let salary=p.salaryDesc||p.salaryShow||p.salary||'';
            if(!salary&&p.salaryMin)salary=p.salaryMin+(p.salaryMax&&p.salaryMax!==p.salaryMin?'-'+p.salaryMax:'')+(p.salaryType===1?'元/天':'元/月');
            add({url:boss?'https://www.zhipin.com/job_detail/'+encodeURIComponent(id)+'.html'+(p.securityId?'?securityId='+encodeURIComponent(p.securityId):''):'https://www.nowcoder.com/jobs/detail/'+encodeURIComponent(id),title:String(title),company:String(company),salary:String(salary),location:String(p.cityName||p.jobCity||p.location||''),description:String(description),experience:String(p.jobExperience||'')});
          }
        }
        for(const child of Object.values(value))visit(child,depth+1);
      };
       for(const state of [window.__INITIAL_STATE__,window.__NEXT_DATA__,window.__PRELOADED_STATE__,window.__NUXT__])visit(state,0);
       for(const script of document.querySelectorAll('script[type="application/json"],script#__NEXT_DATA__')){
         const body=script.textContent||'';
         if(body.length>4000000)continue;
         try{visit(JSON.parse(body),0);}catch{}
       }
      if(detail(location.href)){
        const title=text(document,['.job-banner .name h1','.job-name h1','.job-title','h1']);
        add({url:location.href,title,company:text(document,['.company-info .name','.company-name','.company-info h3 a']),salary:text(document,['.job-banner .salary','.job-salary','.salary']),location:text(document,['.job-area','.job-location']),description:text(document,['.job-sec-text','.job-description','.job-detail .text','.job-detail'])});
      }
      return {url:location.href,pageTitle:document.title,jobs};
    })()
    """;

    private async Task OpenPlatformCaptureAsync(string provider,string keyword="",Action<CoreWebView2>? verificationSetup=null)
    {
        if(provider is not ("boss" or "nowcoder"))throw new ArgumentException("未知招聘平台");
        if(verificationSetup is not null&&!VerificationSession)throw new InvalidOperationException("网页夹具仅可用于隔离验证。");
        string name=provider=="boss"?"BOSS直聘":"牛客";
        string start=provider=="boss"?"https://www.zhipin.com/web/geek/jobs?query="+Uri.EscapeDataString(keyword):"https://www.nowcoder.com/jobs/intern/center";
        var items=new ObservableCollection<Job>();var byUrl=new Dictionary<string,Job>();
        var list=new ListView {ItemsSource=items,SelectionMode=ListViewSelectionMode.None};
        list.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="4" Spacing="3"><TextBlock Text="{Binding Title}" FontWeight="SemiBold"/><TextBlock Text="{Binding Company}" FontSize="12"/><StackPanel Orientation="Horizontal" Spacing="10"><TextBlock Text="{Binding Location}" FontSize="12"/><TextBlock Text="{Binding Salary}" FontSize="12"/></StackPanel></StackPanel></DataTemplate>
        """);
        var status=Text("正常浏览后点击“读取当前页并收藏”。需要连续采集时再开启自动采集。",12);
        var toggle=new ToggleSwitch {OnContent="自动采集已开",OffContent="自动采集已关",IsOn=false};
        ToolTipService.SetToolTip(toggle,"开启后，正常浏览和翻页返回的职位会直接加入收藏。");
        var address=Text(start,11);address.MaxLines=1;address.TextTrimming=TextTrimming.CharacterEllipsis;
        bool closed=false,ending=false,reading=false,initialized=false,discard=false;int activeResponses=0,epoch=0;string captureFailure="";
        var controls=Stack(6);var toolbar=Row();controls.Children.Add(toolbar);using var viewport=new CaptureViewport(shell.XamlRoot,controls,status,address,list);
        var web=viewport.Web;
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title=name+" · 浏览并直接收藏",Content=viewport.Content,PrimaryButtonText="结束并查看收藏",CloseButtonText="返回"};
        dialog.Resources["ContentDialogMaxWidth"]=1460d;
        var session=new CapturedFavorites(job=>api.Post<CollectionResult<Job>>("favorites/collect",job));
        Task activeRead=Task.CompletedTask;
        Button retry=null!,clear=null!;
        var read=Button("读取当前页并收藏",ReadCurrent);retry=Button("重试未保存职位",()=>session.SaveAsync(true));
        clear=Button("清空列表",()=>{epoch++;items.Clear();byUrl.Clear();UpdateStatus();return Task.CompletedTask;});
        ToolTipService.SetToolTip(clear,"仅清空当前窗口的展示，不删除职位收藏。");
        toolbar.Children.Add(toggle);toolbar.Children.Add(read);toolbar.Children.Add(clear);controls.Children.Add(retry);
        var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
        void UpdateStatus()
        {
            if(closed)return;
            if(session.Error.Length>0&&toggle.IsOn)toggle.IsOn=false;
            status.Text=$"本次新增 {session.Added} 条 · 已有收藏 {session.Existing} 条 · 待保存 {session.Pending} 条。"+(reading?"正在读取…":session.Busy?"正在保存…":toggle.IsOn?"自动采集已开启":"点击读取当前页可直接收藏。")+(session.Error.Length>0?"\n保存未完成："+session.Error:captureFailure.Length>0?"\n"+captureFailure:"");
            ToolTipService.SetToolTip(status,status.Text);
            if(items.Count>=PlatformCapture.MaxJobs)status.Text+="\n本次列表已达 500 条；可清空列表后继续读取，已存收藏保留。";
            retry.Visibility=session.Error.Length>0?Visibility.Visible:Visibility.Collapsed;
            retry.IsEnabled=!session.Busy;clear.IsEnabled=session.Pending==0&&!session.Busy&&!reading;
            dialog.IsPrimaryButtonEnabled=!reading&&!session.Busy&&session.Pending==0;
            dialog.SecondaryButtonText=session.Error.Length>0&&session.Pending>0?"放弃未保存":"";
        }
        void PauseForFailure(string message){captureFailure=message;toggle.IsOn=false;UpdateStatus();}
        async Task Add(IEnumerable<Job> jobs)
        {
            foreach(var candidate in jobs) {
                var job=candidate;
                if(byUrl.TryGetValue(job.CanonicalUrl,out var previous)) {
                    job=PlatformCapture.Merge(previous,job);int index=items.IndexOf(previous);byUrl[job.CanonicalUrl]=job;if(index>=0)items[index]=job;
                }else if(items.Count<PlatformCapture.MaxJobs){byUrl[job.CanonicalUrl]=job;items.Add(job);}
                else continue;
                session.Accept(job);
            }
            await session.SaveAsync();UpdateStatus();
        }
        session.Changed=UpdateStatus;
        session.Saved=job=>{if(closed)return;string key=PlatformCapture.Canonical(job.Url);if(byUrl.TryGetValue(key,out var previous)){int index=items.IndexOf(previous);byUrl[key]=job;if(index>=0)items[index]=job;}};
        Task ReadCurrent(){if(reading)return activeRead;activeRead=ReadDom();return activeRead;}
        async Task ReadDom()
        {
            if(closed||ending||web.CoreWebView2 is null||!PlatformCapture.IsPlatformUrl(provider,web.CoreWebView2.Source))return;
            reading=true;int generation=epoch;UpdateStatus();
            try {
                string json=await web.CoreWebView2.ExecuteScriptAsync(PlatformJobsScript);
                if(closed||generation!=epoch)return;
                using var parsed=JsonDocument.Parse(json);
                if(parsed.RootElement.TryGetProperty("pageTitle",out var title)&&PlatformCapture.IsChallenge(title.GetString()??"")){PauseForFailure("页面要求登录或验证，请本人完成后重新读取。已有收藏保留。");return;}
                var found=PlatformCapture.ExtractDom(provider,json);await Add(found);
                if(found.Count==0)captureFailure="当前页面没有可读取的职位，请进入职位列表或详情页后再读取。";
                else captureFailure="";
            }catch(Exception) when(closed) { }
            catch(Exception ex){captureFailure="页面读取未完成："+ex.Message;}
            finally {reading=false;UpdateStatus();}
        }
        async void ResponseReceived(object? sender,CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if(closed||ending||!toggle.IsOn||activeResponses>=3||web.CoreWebView2 is null||!PlatformCapture.IsPlatformUrl(provider,web.CoreWebView2.Source))return;
            string type=e.Response.Headers.Contains("Content-Type")?e.Response.Headers.GetHeader("Content-Type"):"";
            if(!PlatformCapture.IsJobResponseUrl(provider,e.Request.Uri))return;
            string httpFailure=PlatformCapture.FailureMessage(e.Response.StatusCode,"");if(httpFailure!=""){PauseForFailure(httpFailure);return;}
            if(e.Response.StatusCode!=200||!PlatformCapture.CanReadResponse(provider,e.Request.Uri,type))return;
            int generation=epoch;activeResponses++;
            try {
                using var browserStream=await e.Response.GetContentAsync();if(browserStream is null)return;
                using var stream=browserStream.AsStreamForRead();using var buffer=new MemoryStream();var chunk=new byte[16384];
                while(true){int count=await stream.ReadAsync(chunk);if(count==0)break;if(buffer.Length+count>PlatformCapture.MaxPayloadBytes)return;await buffer.WriteAsync(chunk.AsMemory(0,count));}
                if(!closed&&!ending&&toggle.IsOn&&generation==epoch){string json=Encoding.UTF8.GetString(buffer.ToArray()),failure=PlatformCapture.FailureMessage(e.Response.StatusCode,json);if(failure!="")PauseForFailure(failure);else await Add(PlatformCapture.ExtractJson(provider,e.Request.Uri,json));}
            }catch(Exception) when(closed) { }
            catch(Exception ex){captureFailure="部分职位响应读取未完成："+ex.Message;UpdateStatus();}
            finally {activeResponses--;}
        }
        toggle.Toggled+=async (_,_)=>{epoch++;if(toggle.IsOn)captureFailure="";UpdateStatus();if(toggle.IsOn)await ReadCurrent();};
        timer.Tick+=async (_,_)=>{if(toggle.IsOn)await ReadCurrent();};
        dialog.SecondaryButtonClick+=(_,_)=>discard=true;
        dialog.Closing+=async (_,args)=>{
            var deferral=args.GetDeferral();ending=true;timer.Stop();
            try {await activeRead;await session.SaveAsync();if(session.Pending>0&&!discard){args.Cancel=true;ending=false;captureFailure="还有职位未保存，请重试，或选择放弃未保存的职位。";timer.Start();UpdateStatus();}}
            finally {deferral.Complete();}
        };
        dialog.Opened+=async (_,_)=>{
            if(initialized)return;initialized=true;
            try {
                webEnvironment??=CreateWebEnvironment();await web.EnsureCoreWebView2Async(await webEnvironment);if(closed)return;
                var core=web.CoreWebView2!;core.Settings.AreDevToolsEnabled=false;core.Settings.IsZoomControlEnabled=false;
                core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&e.Uri!="about:blank")e.Cancel=true;};
                core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase))core.Navigate(e.Uri);};
                core.NavigationCompleted+=async (_,_)=>{if(closed)return;address.Text=core.Source;await viewport.FitAsync();if(!closed&&toggle.IsOn)await ReadCurrent();};
                core.WebResourceResponseReceived+=ResponseReceived;verificationSetup?.Invoke(core);core.Navigate(start);timer.Start();
            }catch(Exception ex){captureFailure="浏览器启动失败："+ex.Message;UpdateStatus();}
        };
        UpdateStatus();ContentDialogResult choice;
        try {choice=await dialog.ShowAsync();}
        finally {closed=true;epoch++;timer.Stop();viewport.Dispose();if(web.CoreWebView2 is not null)web.CoreWebView2.WebResourceResponseReceived-=ResponseReceived;web.Close();}
        if(choice==ContentDialogResult.Primary){favoriteQuery="";favoriteSource=name;await Navigate("favorites");}
        else if(currentPage=="favorites")await Navigate("favorites");
        if(session.Added+session.Existing>0)Success("采集职位已加入收藏",$"本次新增 {session.Added} 条；已有收藏 {session.Existing} 条，原有备注和内容保留。"+(session.Pending>0?$"已放弃 {session.Pending} 条未保存职位。":""));
    }

}
