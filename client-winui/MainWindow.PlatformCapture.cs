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
    // Only the explicitly opened detail is a save target. Recommendations and list rows are excluded.
    private const string PlatformJobsScript="""
    (()=>{
      const boss=/(^|\.)zhipin\.com$/.test(location.hostname);
      const detail=url=>{try{const u=new URL(url,location.href);return u.protocol==='https:'&&(boss?/(^|\.)zhipin\.com$/.test(u.hostname)&&/^\/job_detail\/[\w-]+\.html$/.test(u.pathname):/(^|\.)nowcoder\.com$/.test(u.hostname)&&/^\/jobs\/detail\/[\w-]+\/?$/.test(u.pathname));}catch{return false;}};
      const visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
      const clean=s=>String(s||'').replace(/[\uE000-\uF8FF\uFFFD]/g,'').trim();
      const text=(root,selectors)=>{for(const selector of selectors){const e=[...root.querySelectorAll(selector)].find(e=>visible(e)&&e.innerText.trim()&&!e.closest('.recommend-job,.recommend-list,.job-recommend'));if(e)return clean(e.innerText);}return '';};
      const descriptions=['.job-sec-text','.job-description','.job-desc','.job-detail .text','[itemprop="description"]','.position-description'];
      const titles=['.job-banner .name h1','.job-banner .job-name','.job-header .job-name','.job-name h1','.job-title','.job-name','h1'];
      let root=document,target=detail(location.href)?location.href:'';
      const empty=()=>({url:location.href,pageTitle:document.title,jobs:[],detailVisible:false,targetUrl:''});
      if(!target){
        const panels=[...document.querySelectorAll('.job-detail-box,.job-detail-container,.job-detail-panel,.job-detail-wrap,.job-detail-modal,.job-detail-popup,.job-detail,[role="dialog"]')].filter(e=>visible(e)&&text(e,descriptions)&&text(e,titles));
        const outer=panels.filter(e=>!panels.some(p=>p!==e&&p.contains(e)));
        if(outer.length!==1)return empty();root=outer[0];
        const links=[...root.querySelectorAll('a[href]')].filter(a=>detail(a.href)&&!a.closest('.recommend-job,.recommend-list,.job-recommend,.job-card-wrapper,.job-card-box'));
        const own=root.getAttribute('data-url')||root.getAttribute('data-job-url'),id=root.getAttribute('data-encrypt-job-id')||root.getAttribute('data-job-id');
        const urls=[...new Set(links.map(a=>new URL(a.href).pathname))];
        if(own&&detail(own))target=new URL(own,location.href).href;
        else if(id&&/^[\w-]+$/.test(id))target=boss?'https://www.zhipin.com/job_detail/'+id+'.html':'https://www.nowcoder.com/jobs/detail/'+id;
        else if(urls.length===1)target=links[0].href;
        else {
          const active=[...document.querySelectorAll('.job-card-wrapper.active,.job-card-box.active,.job-card-item.active,.job-item.active,.job-card-wrapper.selected,.job-card-box.selected')].filter(visible);
          if(active.length===1&&text(active[0],['.job-name','.job-title','h3'])===text(root,titles))target=active[0].querySelector(boss?'a[href*="/job_detail/"]':'a[href*="/jobs/detail/"]')?.href||'';
        }
      }
      if(!detail(target))return empty();
      const contents=[];
      for(const selector of descriptions){const nodes=[...root.querySelectorAll(selector)].filter(e=>visible(e)&&!e.closest('.recommend-job,.recommend-list,.job-recommend'));if(nodes.length){contents.push(...nodes.map(e=>clean(e.innerText)).filter(Boolean));break;}}
      const requirements=text(root,['.job-requirements','.job-requirement','.qualification','.position-requirement','[itemprop="qualifications"]']);
      if(requirements&&!contents.some(s=>s.includes(requirements)))contents.push('任职要求\n'+requirements);
      const job={url:target,title:text(root,titles),company:text(root,['.job-banner .company-name','.company-info .name','.job-company .company-name','.company-name','.company-info h3 a','.sider-company .company-info a','[itemprop="hiringOrganization"]']),salary:text(root,['.job-banner .salary','.job-header .salary','.job-salary','.salary']),location:text(root,['.job-location .location-address','.location-address','.job-area','.job-location','.job-address','[itemprop="jobLocation"]']),experience:text(root,['.job-banner .text','.job-header .job-info','.job-require']),description:[...new Set(contents)].join('\n').slice(0,30000)};
      const salaryNode=[...root.querySelectorAll('.job-banner .salary,.job-header .salary,.job-salary,.salary')].find(visible);
      if(salaryNode&&/[\uE000-\uF8FF\uFFFD]/.test(salaryNode.innerText))job.salary='';
      let visited=0;
      const same=url=>{try{return new URL(url,location.href).pathname===new URL(target).pathname;}catch{return false;}};
      const fill=p=>{for(const key of ['title','company','salary','location','experience'])if(!job[key]&&p[key])job[key]=clean(p[key]);if(String(p.description||'').length>job.description.length)job.description=String(p.description).slice(0,30000);};
      const visit=(v,depth)=>{
        if(!v||typeof v!=='object'||depth>24||++visited>20000)return;
        if(!Array.isArray(v)){
          const p=v.jobInfo||v,id=boss?p.encryptJobId:(p.jobId||p.id),url=id?(boss?'https://www.zhipin.com/job_detail/'+id+'.html':'https://www.nowcoder.com/jobs/detail/'+id):'';
          if(url&&same(url)){
            let description=p.postDescription||p.jobDescription||p.description||p.jobDesc||p.responsibility||p.positionDesc||'';
            const requirements=p.requirements||p.qualification||p.qualifications||p.jobRequirements||'';
            if(requirements&&!String(description).includes(requirements))description+='\n任职要求\n'+requirements;
            if(typeof p.ext==='string'&&p.ext.length<=100000){try{const ext=JSON.parse(p.ext);if(!description)description=[ext.infos,ext.requirements].filter(Boolean).join('\n');}catch{}}
            fill({title:p.jobName||p.jobTitle||p.positionName,company:p.brandName||p.companyName||p.companyShortName||(p.recommendInternCompany||{}).companyName||(v.brandComInfo||{}).brandName,salary:p.salaryDesc||p.salaryShow||p.salary,location:p.jobAddress||p.cityName||p.jobCity||p.location,experience:p.jobExperience,description});
          }
          if(p['@type']==='JobPosting'&&p.url&&same(p.url))fill({title:p.title,description:[p.description,p.responsibilities,p.qualifications].filter(Boolean).join('\n'),company:p.hiringOrganization?.name,location:p.jobLocation?.address?.streetAddress||p.jobLocation?.address?.addressLocality});
        }
        for(const child of Object.values(v))visit(child,depth+1);
      };
      for(const state of [window.__INITIAL_STATE__,window.__NEXT_DATA__,window.__PRELOADED_STATE__,window.__NUXT__])visit(state,0);
      for(const script of document.querySelectorAll('script[type="application/json"],script[type="application/ld+json"],script#__NEXT_DATA__')){const body=script.textContent||'';if(body.length>4000000)continue;try{visit(JSON.parse(body),0);}catch{}}
      return {url:location.href,pageTitle:document.title,targetUrl:target,detailVisible:true,jobs:job.title?[job]:[]};
    })()
    """;

    private async Task OpenPlatformCaptureAsync(string provider,string keyword="",Action<CoreWebView2>? verificationSetup=null)
    {
        if(provider is not ("boss" or "nowcoder"))throw new ArgumentException("未知招聘平台");
        if(verificationSetup is not null&&!VerificationSession)throw new InvalidOperationException("网页夹具仅可用于隔离验证。");
        string name=provider=="boss"?"BOSS直聘":"牛客";
        string start=provider=="boss"?"https://www.zhipin.com/web/geek/jobs?query="+Uri.EscapeDataString(keyword):"https://www.nowcoder.com/jobs/intern/center";
        var items=new ObservableCollection<Job>();var byUrl=new Dictionary<string,Job>();var details=new Dictionary<string,Job>();
        var list=new ListView {ItemsSource=items,SelectionMode=ListViewSelectionMode.None};
        list.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="4" Spacing="3"><TextBlock Text="{Binding Title}" FontWeight="SemiBold"/><TextBlock Text="{Binding Company}" FontSize="12"/><StackPanel Orientation="Horizontal" Spacing="10"><TextBlock Text="{Binding Location}" FontSize="12"/><TextBlock Text="{Binding Salary}" FontSize="12"/></StackPanel><TextBlock Text="{Binding Description}" FontSize="12" MaxLines="3" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"/></StackPanel></DataTemplate>
        """);
        var status=Text("打开完整职位详情，再点击“收藏当前职位”。浏览和翻页不会自动收藏。",12);
        var address=Text(start,11);address.MaxLines=1;address.TextTrimming=TextTrimming.CharacterEllipsis;
        bool closed=false,ending=false,reading=false,initialized=false,discard=false;int activeResponses=0,epoch=0;string captureFailure="";
        var controls=Stack(6);var toolbar=Row();controls.Children.Add(toolbar);using var viewport=new CaptureViewport(shell.XamlRoot,controls,status,address,list);
        var web=viewport.Web;
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title=name+" · 收藏当前职位",Content=viewport.Content,PrimaryButtonText="结束并查看收藏",CloseButtonText="返回"};
        dialog.Resources["ContentDialogMaxWidth"]=1460d;
        var session=new CapturedFavorites(job=>api.Post<CollectionResult<Job>>("favorites/collect-detail",job));
        Task activeRead=Task.CompletedTask;
        Button read=null!,retry=null!,clear=null!;
        read=Button("收藏当前职位",ReadCurrent);retry=Button("重试未保存职位",()=>session.SaveAsync(true));
        clear=Button("清空记录",()=>{items.Clear();byUrl.Clear();UpdateStatus();return Task.CompletedTask;});
        ToolTipService.SetToolTip(clear,"仅清空本次展示，不删除已保存的收藏。");
        toolbar.Children.Add(read);toolbar.Children.Add(clear);controls.Children.Add(retry);
        void UpdateStatus()
        {
            if(closed)return;
            status.Text=$"本次新增 {session.Added} 条 · 补全 {session.Updated} 条 · 已收藏 {session.Existing} 条。"+(reading?"正在读取当前详情…":session.Busy?"正在保存…":"仅收藏你打开的当前职位。")+(session.Error.Length>0?"\n保存未完成："+session.Error:captureFailure.Length>0?"\n"+captureFailure:"");
            ToolTipService.SetToolTip(status,status.Text);
            retry.Visibility=session.Error.Length>0?Visibility.Visible:Visibility.Collapsed;
            retry.IsEnabled=!session.Busy;read.IsEnabled=!reading&&!session.Busy&&session.Pending==0;clear.IsEnabled=session.Pending==0&&!session.Busy&&!reading;
            dialog.IsPrimaryButtonEnabled=!reading&&!session.Busy&&session.Pending==0;
            dialog.SecondaryButtonText=session.Error.Length>0&&session.Pending>0?"放弃未保存":"";
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
                var job=PlatformCapture.ExtractCurrentDetail(provider,json,details.Values);
                if(job is null){captureFailure="未读取到完整职位正文。请打开具体职位详情并等待加载；登录／验证需本人完成。本次没有收藏职位。";return;}
                string current=await web.CoreWebView2.ExecuteScriptAsync(PlatformJobsScript);
                using var parsed=JsonDocument.Parse(current);
                if(closed||generation!=epoch||!parsed.RootElement.TryGetProperty("targetUrl",out var target)||target.GetString() is not {} url||!PlatformCapture.IsDetailUrl(provider,url)||PlatformCapture.Canonical(url)!=job.CanonicalUrl){captureFailure="当前职位已切换，请重新点击收藏。";return;}
                captureFailure="";
                if(byUrl.TryGetValue(job.CanonicalUrl,out var previous)){int index=items.IndexOf(previous);byUrl[job.CanonicalUrl]=job;if(index>=0)items[index]=job;}
                else if(items.Count<PlatformCapture.MaxJobs){byUrl[job.CanonicalUrl]=job;items.Add(job);}
                session.Accept(job);await session.SaveAsync();
            }catch(Exception) when(closed) { }
            catch(Exception ex){captureFailure="页面读取未完成："+ex.Message;}
            finally {reading=false;UpdateStatus();}
        }
        // Optional detail response enrichment stays in memory and never saves by itself.
        async void ResponseReceived(object? sender,CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if(closed||ending||activeResponses>=3||web.CoreWebView2 is null||!PlatformCapture.IsPlatformUrl(provider,web.CoreWebView2.Source))return;
            string type=e.Response.Headers.Contains("Content-Type")?e.Response.Headers.GetHeader("Content-Type"):"";
            if(e.Response.StatusCode!=200||!PlatformCapture.CanReadResponse(provider,e.Request.Uri,type)||!System.Text.RegularExpressions.Regex.IsMatch(new Uri(e.Request.Uri).AbsolutePath,@"(?i)(job|position)[/_-]?detail|detail[/_-]?(job|position)"))return;
            int generation=epoch;activeResponses++;
            try {
                using var browserStream=await e.Response.GetContentAsync();if(browserStream is null)return;
                using var stream=browserStream.AsStreamForRead();using var buffer=new MemoryStream();var chunk=new byte[16384];
                while(true){int count=await stream.ReadAsync(chunk);if(count==0)break;if(buffer.Length+count>PlatformCapture.MaxPayloadBytes)return;await buffer.WriteAsync(chunk.AsMemory(0,count));}
                if(!closed&&generation==epoch)foreach(var job in PlatformCapture.ExtractJson(provider,e.Request.Uri,Encoding.UTF8.GetString(buffer.ToArray())).Where(PlatformCapture.HasDetail)){if(details.Count>=20&&!details.ContainsKey(job.CanonicalUrl))details.Remove(details.Keys.First());details[job.CanonicalUrl]=job;}
            }catch(Exception) { /* DOM capture remains available if optional enrichment fails. */ }
            finally {activeResponses--;}
        }
        dialog.SecondaryButtonClick+=(_,_)=>discard=true;
        dialog.Closing+=async (_,args)=>{
            var deferral=args.GetDeferral();ending=true;
            try {await activeRead;await session.SaveAsync();if(session.Pending>0&&!discard){args.Cancel=true;ending=false;captureFailure="当前职位尚未保存，请重试，或选择放弃未保存。";UpdateStatus();}}
            finally {deferral.Complete();}
        };
        dialog.Opened+=async (_,_)=>{
            if(initialized)return;initialized=true;
            try {
                webEnvironment??=CreateWebEnvironment();await web.EnsureCoreWebView2Async(await webEnvironment);if(closed)return;
                var core=web.CoreWebView2!;core.Settings.AreDevToolsEnabled=false;core.Settings.IsZoomControlEnabled=false;
                core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&e.Uri!="about:blank")e.Cancel=true;else {epoch++;details.Clear();}};
                core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase))core.Navigate(e.Uri);};
                core.NavigationCompleted+=async (_,_)=>{if(closed)return;address.Text=core.Source;await viewport.FitAsync();};
                core.WebResourceResponseReceived+=ResponseReceived;verificationSetup?.Invoke(core);core.Navigate(start);
            }catch(Exception ex){captureFailure="浏览器启动失败："+ex.Message;UpdateStatus();}
        };
        UpdateStatus();ContentDialogResult choice;
        try {choice=await dialog.ShowAsync();}
        finally {closed=true;epoch++;viewport.Dispose();if(web.CoreWebView2 is not null){web.CoreWebView2.WebResourceResponseReceived-=ResponseReceived;if(!VerificationSession)await MaintainBrowserDiskCache(web.CoreWebView2);}web.Close();}
        if(choice==ContentDialogResult.Primary||session.Added+session.Updated>0)ResetFavoriteView();
        if(choice==ContentDialogResult.Primary){await Navigate("favorites");}
        else if(currentPage=="favorites")await Navigate("favorites");
        if(session.Added+session.Existing+session.Updated>0)Success("当前职位已收藏",$"本次新增 {session.Added} 条；补全 {session.Updated} 条；已有收藏 {session.Existing} 条。原有备注保留。"+(session.Pending>0?$"已放弃 {session.Pending} 条未保存职位。":""));
    }
}
