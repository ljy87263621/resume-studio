using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task CaptureLayoutTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"capture-layout");Directory.CreateDirectory(output);
        var checks=new List<string>();var measurements=new List<object>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        async Task Resize(double width,double height){double dpi=shell.XamlRoot.RasterizationScale;AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width*dpi),(int)(height*dpi)));await Task.Delay(450);}
        try {
            await Resize(1400,900);
            var results=new ListView {ItemsSource=Enumerable.Range(1,50).Select(i=>"测试职位 "+i).ToList(),SelectionMode=ListViewSelectionMode.Multiple};
            var controls=Row();controls.Children.Add(new ToggleSwitch {OnContent="自动采集已开",IsOn=true});controls.Children.Add(Button("读取当前页面",()=>Task.CompletedTask));controls.Children.Add(Button("全选",()=>{results.SelectAll();return Task.CompletedTask;}));controls.Children.Add(Button("清空结果",()=>Task.CompletedTask));
            using var viewport=new CaptureViewport(shell.XamlRoot,controls,Text("本次已采集 50 条 · 选中 0 条。正在自动采集",12),Text("https://www.zhipin.com/web/geek/jobs?query=embedded",11),results);
            var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title="招聘采集布局验证",Content=viewport.Content,PrimaryButtonText="核对选中职位",CloseButtonText="返回"};dialog.Resources["ContentDialogMaxWidth"]=1460d;
            var loaded=new TaskCompletionSource();
            dialog.Opened+=async (_,_)=> {
                try {
                    await InitializeWeb(viewport.Web);
                    viewport.Web.CoreWebView2.NavigationCompleted+=(_,_)=>loaded.TrySetResult();
                    viewport.Web.NavigateToString("""
                    <html><head><meta charset="utf-8"><style>
                    *{box-sizing:border-box}html,body{margin:0}body{min-width:1184px;font:18px Arial;background:#eef7f7}
                    header{height:72px;background:#203f46;color:#65c9c6;padding:20px;font-size:28px}
                    main{width:1184px;margin:auto;padding:24px}input{width:640px;height:50px;font:20px Arial;padding:10px}
                    .filters{display:flex;gap:18px;margin:20px 0}.filters span{padding:14px;background:white}
                    .jobs{display:grid;grid-template-columns:440px 1fr;gap:20px}.job,.details{padding:22px;background:white;margin-bottom:12px}
                    </style></head><body><header>BOSS 直聘 · 桌面宽度布局测试</header><main><input id="search" value="嵌入式"><div class="filters"><span>广州</span><span>职位类型</span><span>薪资待遇</span><span>工作经验</span><span>学历要求</span></div><div class="jobs"><div id="cards"></div><div class="details"><h2>嵌入式软件开发</h2><p>验证桌面网站适合宽度、页面点击和独立滚动。</p></div></div></main><script>cards.innerHTML=Array.from({length:30},(_,i)=>`<div class="job"><b>测试职位 ${i+1}</b><p>C / Linux / RTOS</p></div>`).join('')</script></body></html>
                    """);
                }catch(Exception ex){loaded.TrySetException(ex);}
            };
            var shown=dialog.ShowAsync();await FeatureDialog("招聘采集布局验证");await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            foreach(var size in new[] {(1400d,900d),(1000d,720d),(760d,650d),(1400d,900d)}) {
                await Resize(size.Item1,size.Item2);await viewport.FitAsync();await Task.Delay(200);
                string raw=await viewport.Web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({width:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth,zoom:Number(document.documentElement.style.zoom),height:innerHeight})");
                using var dimensions=JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                var d=dimensions.RootElement;double zoom=d.GetProperty("zoom").GetDouble();
                Check(viewport.Content.ActualWidth<=shell.XamlRoot.Size.Width&&viewport.Content.ActualHeight<shell.XamlRoot.Size.Height,"capture content stays within the resized window "+size);
                Check(viewport.Web.ActualHeight>130&&results.ActualHeight>40&&viewport.Web.ActualWidth<=viewport.Content.ActualWidth,"browser and result list have independent finite viewports "+size);
                Check(d.GetProperty("scroll").GetDouble()<=d.GetProperty("width").GetDouble()+3,"fixed desktop page fits without horizontal overflow "+size);
                measurements.Add(new {windowWidth=size.Item1,windowHeight=size.Item2,root=shell.XamlRoot.Size,contentWidth=viewport.Content.ActualWidth,contentHeight=viewport.Content.ActualHeight,webWidth=viewport.Web.ActualWidth,webHeight=viewport.Web.ActualHeight,listHeight=results.ActualHeight,zoom});
            }
            await viewport.Web.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0,500)");results.ScrollIntoView(results.Items[^1]);await Task.Delay(150);
            Check(await viewport.Web.CoreWebView2.ExecuteScriptAsync("scrollY >= 490")=="true","scrolling the result list does not reset website scrolling");
            results.SelectedIndex=0;double beforeHeight=viewport.Web.ActualHeight;await FeatureClick(dialog,"收起结果");await Task.Delay(300);
            Check(viewport.Web.ActualHeight>beforeHeight+50&&results.Visibility==Microsoft.UI.Xaml.Visibility.Collapsed,"collapsing results gives the browser additional height");
            await FeatureClick(dialog,"展开结果");await Task.Delay(300);Check(results.SelectedItems.Count==1&&results.Visibility==Microsoft.UI.Xaml.Visibility.Visible,"reopening results preserves selected jobs");
            viewport.Zoom.SelectedIndex=1;await viewport.FitAsync();Check(await viewport.Web.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.zoom === '1'")=="true","100 percent restores the original desktop page scale");
            viewport.Zoom.SelectedIndex=3;await viewport.FitAsync();Check(await viewport.Web.CoreWebView2.ExecuteScriptAsync("Number(document.documentElement.style.zoom) === .8")=="true","manual 80 percent zoom is applied");
            await viewport.Web.CoreWebView2.ExecuteScriptAsync("scrollTo(0,0)");
            using var point=JsonDocument.Parse(JsonSerializer.Deserialize<string>(await viewport.Web.CoreWebView2.ExecuteScriptAsync("JSON.stringify((()=>{const r=document.querySelector('#search').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})())"))!);
            double x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble();
            foreach(string type in new[] {"mousePressed","mouseReleased"})await viewport.Web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new {type,x,y,button="left",clickCount=1}));
            Check(await viewport.Web.CoreWebView2.ExecuteScriptAsync("document.activeElement.id === 'search'")=="true","scaled website controls retain correct hit testing");
            viewport.Zoom.SelectedIndex=0;await viewport.FitAsync();
            var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(output);var screenshot=await folder.CreateFileAsync("browser-fit.png",Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using(var stream=await screenshot.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))await viewport.Web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,stream);
            await SaveNativePreview(Path.Combine(output,"capture-dialog.png"));await FeatureClick(dialog,"返回");await shown;viewport.Dispose();viewport.Web.Close();
            Check(!Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot).Any(p=>p.Child is ContentDialog),"capture dialog closes normally after repeated resizing");
            await File.WriteAllTextAsync(Path.Combine(output,"capture-layout-result.json"),JsonSerializer.Serialize(new {status="passed",checks,measurements},BackendClient.Json));
        }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"capture-layout-result.json"),JsonSerializer.Serialize(new {status="failed",checks,measurements,error=ex.ToString()},BackendClient.Json));}
        finally {closing=true;api.Dispose();Close();}
    }
}
