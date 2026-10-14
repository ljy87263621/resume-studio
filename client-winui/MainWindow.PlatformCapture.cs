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

    private async Task OpenPlatformCaptureAsync(string provider,string keyword="")
    {
        if(provider is not ("boss" or "nowcoder")) throw new ArgumentException("未知招聘平台");
        string name=provider=="boss"?"BOSS直聘":"牛客";
        string start=provider=="boss"?"https://www.zhipin.com/web/geek/jobs?query="+Uri.EscapeDataString(keyword):"https://www.nowcoder.com/jobs/intern/center";
        var items=new ObservableCollection<Job>(); var byUrl=new Dictionary<string,Job>();
        var list=new ListView {ItemsSource=items,SelectionMode=ListViewSelectionMode.Multiple};
        list.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="4" Spacing="3"><TextBlock Text="{Binding Title}" FontWeight="SemiBold"/><TextBlock Text="{Binding Company}" FontSize="12"/><StackPanel Orientation="Horizontal" Spacing="10"><TextBlock Text="{Binding Location}" FontSize="12"/><TextBlock Text="{Binding Salary}" FontSize="12"/></StackPanel></StackPanel></DataTemplate>
        """);
        var status=Text("请在页面内本人登录，然后正常搜索和翻页。页面返回的职位会自动加入下方列表；验证需要本人完成。",12);
        var toggle=new ToggleSwitch {OnContent="自动采集已开",OffContent="自动采集已暂停",IsOn=true};
        ToolTipService.SetToolTip(toggle,"自动采集本次正常浏览返回的职位");
        var address=Text(start,11); address.MaxLines=1; address.TextTrimming=TextTrimming.CharacterEllipsis;
        bool closed=false,reading=false,initialized=false; int activeResponses=0,epoch=0; string captureFailure="";
        var controls=Row();
        using var viewport=new CaptureViewport(shell.XamlRoot,controls,status,address,list);
        var web=viewport.Web;
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title=name+" · 登录后自动采集",Content=viewport.Content,PrimaryButtonText="核对选中职位",CloseButtonText="返回",IsPrimaryButtonEnabled=false};
        dialog.Resources["ContentDialogMaxWidth"]=1460d;
        controls.Children.Add(toggle);
        controls.Children.Add(Button("读取当前页面",ReadDom));
        controls.Children.Add(Button("全选",()=> {list.SelectAll(); return Task.CompletedTask;}));
        controls.Children.Add(Button("清空结果",()=> {epoch++; items.Clear(); byUrl.Clear(); UpdateStatus(); return Task.CompletedTask;}));
        var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
        void UpdateStatus() {status.Text=$"本次已采集 {items.Count} 条 · 选中 {list.SelectedItems.Count} 条。{(toggle.IsOn?"正在自动采集":"已暂停")}"+(captureFailure==""?"":"\n"+captureFailure); dialog.IsPrimaryButtonEnabled=list.SelectedItems.Count is >0 and <=50;}
        void PauseForFailure(string message) {captureFailure=message; toggle.IsOn=false; UpdateStatus();}
        void Add(IEnumerable<Job> jobs)
        {
            foreach(var candidate in jobs)
            {
                var job=candidate;
                if(byUrl.TryGetValue(job.CanonicalUrl,out var previous))
                {
                    bool selected=list.SelectedItems.Contains(previous); int index=items.IndexOf(previous);
                    job=PlatformCapture.Merge(previous,job); if(ReferenceEquals(job,previous)) continue;
                    byUrl[job.CanonicalUrl]=job; if(index>=0) items[index]=job; if(selected) list.SelectedItems.Add(job);
                }
                else if(items.Count<PlatformCapture.MaxJobs) {byUrl[job.CanonicalUrl]=job; items.Add(job);}
            }
            UpdateStatus();
        }
        async Task ReadDom()
        {
            if(closed||reading||web.CoreWebView2 is null||!PlatformCapture.IsPlatformUrl(provider,web.CoreWebView2.Source)) return;
            reading=true; int generation=epoch;
            try
            {
                string json=await web.CoreWebView2.ExecuteScriptAsync(PlatformJobsScript);
                if(closed||generation!=epoch) return;
                Add(PlatformCapture.ExtractDom(provider,json));
                using var parsed=JsonDocument.Parse(json);
                if(parsed.RootElement.TryGetProperty("pageTitle",out var title)&&PlatformCapture.IsChallenge(title.GetString()??"")) PauseForFailure("页面要求登录或安全验证，请本人完成后重新开启采集。已有结果保留。");
            }
            catch(Exception) when(closed) { }
            catch(Exception e) {status.Text="当前页面读取未完成："+e.Message;}
            finally {reading=false;}
        }
        async void ResponseReceived(object? sender,CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if(closed||!toggle.IsOn||activeResponses>=3||web.CoreWebView2 is null||!PlatformCapture.IsPlatformUrl(provider,web.CoreWebView2.Source)) return;
            string type=e.Response.Headers.Contains("Content-Type")?e.Response.Headers.GetHeader("Content-Type"):"";
            if(!PlatformCapture.IsJobResponseUrl(provider,e.Request.Uri)) return;
            string httpFailure=PlatformCapture.FailureMessage(e.Response.StatusCode,"");
            if(httpFailure!="") {PauseForFailure(httpFailure); return;}
            if(e.Response.StatusCode!=200||!PlatformCapture.CanReadResponse(provider,e.Request.Uri,type)) return;
            int generation=epoch; activeResponses++;
            try
            {
                using var browserStream=await e.Response.GetContentAsync(); if(browserStream is null) return;
                using var stream=browserStream.AsStreamForRead();
                using var buffer=new MemoryStream(); var chunk=new byte[16384];
                while(true) {int count=await stream.ReadAsync(chunk); if(count==0) break; if(buffer.Length+count>PlatformCapture.MaxPayloadBytes) return; await buffer.WriteAsync(chunk.AsMemory(0,count));}
                if(!closed&&toggle.IsOn&&generation==epoch)
                {
                    string json=Encoding.UTF8.GetString(buffer.ToArray()),failure=PlatformCapture.FailureMessage(e.Response.StatusCode,json);
                    if(failure!="") PauseForFailure(failure); else Add(PlatformCapture.ExtractJson(provider,e.Request.Uri,json));
                }
            }
            catch(Exception) when(closed) { }
            catch(Exception e2) {status.Text="部分职位响应暂未读取；可继续浏览或读取当前页面。"+e2.Message;}
            finally {activeResponses--;}
        }
        list.SelectionChanged+=(_,_)=>UpdateStatus(); toggle.Toggled+=(_,_)=> {epoch++; if(toggle.IsOn) captureFailure=""; UpdateStatus();};
        timer.Tick+=async (_,_)=> {if(toggle.IsOn) await ReadDom();};
        dialog.Opened+=async (_,_)=>
        {
            if(initialized) return; initialized=true;
            try
            {
                webEnvironment??=CreateWebEnvironment(); await web.EnsureCoreWebView2Async(await webEnvironment);
                if(closed) return;
                var core=web.CoreWebView2!; core.Settings.AreDevToolsEnabled=false;
                core.Settings.IsZoomControlEnabled=false;
                core.NavigationStarting+=(_,e)=> {if(!e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&e.Uri!="about:blank") e.Cancel=true;};
                core.NewWindowRequested+=(_,e)=> {e.Handled=true; if(e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase)) core.Navigate(e.Uri);};
                core.NavigationCompleted+=async (_,_)=> {if(closed) return; address.Text=core.Source;await viewport.FitAsync(); if(!closed&&toggle.IsOn) await ReadDom();};
                core.WebResourceResponseReceived+=ResponseReceived;
                core.Navigate(start); timer.Start();
            }
            catch(Exception e) {status.Text="浏览器启动失败："+e.Message;}
        };
        List<Job> selected=[];
        try {if(await dialog.ShowAsync()==ContentDialogResult.Primary) selected=list.SelectedItems.Cast<Job>().Take(50).ToList();}
        finally {closed=true; epoch++; timer.Stop();viewport.Dispose(); if(web.CoreWebView2 is not null) web.CoreWebView2.WebResourceResponseReceived-=ResponseReceived; web.Close();}
        if(selected.Count==0) return;
        var review=Stack(12);
        review.Children.Add(Text($"确认收藏 {selected.Count} 条职位。这里只保存职位数据，账号登录状态留在本机。",14));
        foreach(var job in selected)
        {
            var row=Stack(4); row.Children.Add(Text(job.Title+" · "+job.Company,15,true)); row.Children.Add(Text(job.Location+"  "+job.Salary,12)); row.Children.Add(Text(job.Url,11));
            if(job.Description!="") {var description=Text(job.Description,12); description.MaxLines=5; description.TextTrimming=TextTrimming.CharacterEllipsis; row.Children.Add(description);} else row.Children.Add(Text("当前只有职位摘要；收藏后可用“浏览并读取”补全详情。",12));
            review.Children.Add(Card(row));
        }
        if(await new ContentDialog {XamlRoot=shell.XamlRoot,Title="核对并批量收藏",Content=new ScrollViewer {Content=review,MaxHeight=550},PrimaryButtonText="保存收藏",CloseButtonText="取消"}.ShowAsync()!=ContentDialogResult.Primary) return;
        int saved=0;
        try
        {
            var savedFavorites=await api.Get<List<Job>>("favorites");
            var existing=savedFavorites.Select(j=>j.Id).ToHashSet();
            var existingUrls=savedFavorites.Where(j=>PlatformCapture.IsDetailUrl(provider,j.Url)).Select(j=>PlatformCapture.Canonical(j.Url)).ToHashSet(StringComparer.Ordinal);
            foreach(var job in selected)
            {
                var normalized=await api.Post<CapturePreview>("capture-preview",new {text=job.Url,visibleFields=new {title=job.Title,company=job.Company,location=job.Location,salary=job.Salary,description=job.Description,experience=job.Experience,publishedAt=job.PublishedAt}});
                if(existing.Contains(normalized.Job.Id)||existingUrls.Contains(job.CanonicalUrl)) continue;
                normalized.Job.InfoStatus=job.InfoStatus;
                var result=await api.Post<CollectionResult<Job>>("favorites/collect",normalized.Job);
                existing.Add(result.Record.Id); existingUrls.Add(job.CanonicalUrl); if(result.Created) saved++;
            }
        }
        catch(Exception e) {throw new IOException($"已保存 {saved} 条，后续保存失败。已保存职位保留，可重试未保存部分："+e.Message,e);}
        await Navigate("favorites"); Success("职位已收藏",$"新增 {saved} 条；已收藏的职位已跳过，原有备注和人工修改保留。");
    }
}
