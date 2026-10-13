using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private Task<CoreWebView2Environment>? webEnvironment;
    private async Task<CoreWebView2Environment> CreateWebEnvironment() => await CoreWebView2Environment.CreateWithOptionsAsync("",Path.Combine(api.DataDirectory,"WebView2"),new CoreWebView2EnvironmentOptions());
    private readonly SemaphoreSlim renderLock = new(1,1);
    private string lastRenderedHtml = "";
    private bool renderingPdf;
    private Grid? previewHost;
    private static string PreviewHtml(Resume resume,Template template,bool photoPlaceholder=false)
    {
        string html=Services.ResumeRenderer.Html(resume,template,photoPlaceholder);
        return haruStyle?html.Replace("body{margin:0;background:#e9edf4","body{margin:0;background:transparent"):html;
    }
    private void DetachPreview()
    {
        // Parent can be null while an unloaded Grid still owns the child logically.
        if(preview is not null) previewHost?.Children.Remove(preview);
        previewHost=null;
    }
    private async Task InitializeWeb(WebView2 web)
    {
        if(web.CoreWebView2 is not null) return;
        Directory.CreateDirectory(api.DataDirectory);
        webEnvironment??=CreateWebEnvironment();
        await web.EnsureCoreWebView2Async(await webEnvironment); webInitializations++;
        web.CoreWebView2!.SetVirtualHostNameToFolderMapping("resumestudio.assets",Path.Combine(AppContext.BaseDirectory,"Assets"),CoreWebView2HostResourceAccessKind.Allow);
        web.CoreWebView2!.Settings.AreDefaultContextMenusEnabled=false; web.CoreWebView2.Settings.AreDevToolsEnabled=false;
        web.CoreWebView2.NewWindowRequested+=(_,e)=>e.Handled=true;
        web.CoreWebView2.NavigationStarting+=(_,e)=> {
            if(!e.Uri.StartsWith("about:",StringComparison.OrdinalIgnoreCase)&&!e.Uri.StartsWith("data:text/html",StringComparison.OrdinalIgnoreCase)) e.Cancel=true;
        };
    }
    private async Task RenderPreview()
    {
        if(current is null||preview?.CoreWebView2 is null) return;
        await renderLock.WaitAsync();
        try
        {
            var web=preview; var core=web.CoreWebView2;
            var template=templates.First(t=>t.Id==current.TemplateId);
            var document=showPreviewExamples&&!renderingPdf?Services.ResumeExamples.ForPreview(current,template):current;
            string html=PreviewHtml(document,template);
            if(html==lastRenderedHtml) return;
            string marker=Guid.NewGuid().ToString("N"); renderMarker=marker;
            if(lastRenderedHtml.Length==0)
            {
                var completed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Loaded(CoreWebView2 sender,CoreWebView2NavigationCompletedEventArgs args) => completed.TrySetResult(args.IsSuccess);
                core.NavigationCompleted+=Loaded;
                try
                {
                    previewNavigations++;
                    web.NavigateToString(html.Replace("<head>",$"<head><meta name='resume-render-id' content='{marker}'>"));
                    if(!await completed.Task.WaitAsync(TimeSpan.FromSeconds(12))) throw new IOException("预览初始化失败");
                    await core.ExecuteScriptAsync($$"""
                    (()=>{const meta=document.querySelector('meta[name=resume-render-id]'),marker={{JsonSerializer.Serialize(marker)}};
                      meta.content='';Promise.all([document.fonts.ready,...Array.from(document.images).map(i=>i.decode().catch(()=>{}))])
                        .then(()=>{document.querySelector('article').getBoundingClientRect();meta.content=marker;});return true;})()
                    """);
                }
                finally { core.NavigationCompleted-=Loaded; }
            }
            else
            {
                // DOMParser never executes the supplied document. User text was HTML-escaped by the renderer.
                // Preserve scroll position and the WebView process while updating styles and the A4 article.
                string script=$$"""
                (()=>{
                  const source=new DOMParser().parseFromString({{JsonSerializer.Serialize(html)}},'text/html');
                  const old=document.querySelector('article'),fresh=source.querySelector('article');
                  if(!old||!fresh)return false;
                  const x=scrollX,y=scrollY;
                  document.head.querySelector('style').textContent=source.head.querySelector('style').textContent;
                  old.replaceWith(document.importNode(fresh,true)); document.title=source.title;
                  const meta=document.querySelector('meta[name=resume-render-id]');
                  const marker={{JsonSerializer.Serialize(marker)}};meta.content='';meta.dataset.pending=marker;
                  scrollTo(x,y);
                  Promise.all([document.fonts.ready,...Array.from(document.images).map(i=>i.decode().catch(()=>{}))])
                    .then(()=>{if(meta.dataset.pending===marker){
                      // Flush layout without requestAnimationFrame: hidden/minimized WebViews suspend frame callbacks.
                      document.querySelector('article').getBoundingClientRect();meta.content=marker;
                    } });
                  return true;
                })()
                """;
                if(await core.ExecuteScriptAsync(script)!="true") { lastRenderedHtml=""; throw new IOException("预览文档状态异常，请刷新后重试。"); }
                previewPatches++;
            }
            lastRenderedHtml=html; await UpdateCapacity(web);
        }
        finally { renderLock.Release(); }
    }
    private async Task WaitForPreview()
    {
        if(preview?.CoreWebView2 is null) throw new InvalidOperationException("预览尚未就绪");
        var web=preview; var core=web.CoreWebView2; previewTimer.Stop(); await RenderPreview();
        for(int i=0;i<120;i++)
        {
            if(web!=preview||currentPage!="editor") throw new OperationCanceledException("已切换页面，请返回工作台重新导出。");
            string loaded=await core.ExecuteScriptAsync("document.readyState === 'complete' ? document.querySelector('meta[name=resume-render-id]')?.content : null");
            if(loaded==JsonSerializer.Serialize(renderMarker)) return;
            await Task.Delay(50);
        }
        throw new IOException("预览加载超时，请刷新预览后重试。");
    }
}
