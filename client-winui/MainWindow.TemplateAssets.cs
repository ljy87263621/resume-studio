using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using Windows.Storage;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task GenerateTemplateAssets()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"template-assets");Directory.CreateDirectory(output);
        var web=new WebView2 {Width=794,Height=1123,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};body.Children.Clear();body.Children.Add(web);
        var images=new List<object>();
        try {
            await InitializeWeb(web);
            foreach(var template in templates) foreach(bool photo in template.SupportsPhoto?new[] {false,true}:new[] {false}) {
                string marker=Guid.NewGuid().ToString("N");
                string html=Services.ResumeRenderer.Html(Demo(template),template,photo).Replace("<head>",$"<head><meta name='asset-ready' content='{marker}'>").Replace("</style>","article{margin:0;width:794px;max-width:794px;min-height:1123px}body{background:white}</style>");
                var loaded=new TaskCompletionSource<bool>(); void Loaded(CoreWebView2 _,CoreWebView2NavigationCompletedEventArgs args)=>loaded.TrySetResult(args.IsSuccess);
                web.CoreWebView2!.NavigationCompleted+=Loaded;
                try {web.NavigateToString(html);if(!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)))throw new IOException("Template navigation failed");}
                finally {web.CoreWebView2.NavigationCompleted-=Loaded;}
                await web.CoreWebView2.ExecuteScriptAsync("document.fonts.ready.then(()=>document.querySelector('meta[name=asset-ready]').dataset.fonts='ready')");
                bool ready=false;for(int n=0;n<200;n++){if(await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('meta[name=asset-ready]')?.dataset.fonts==='ready'")=="true"){ready=true;break;}await Task.Delay(50);}if(!ready)throw new IOException("Template fonts did not finish loading");
                if(await web.CoreWebView2.ExecuteScriptAsync("Array.from(document.fonts).some(f=>f.family==='MiSans'&&f.status==='loaded')")!="true")throw new IOException("Bundled MiSans did not load");
                if(await web.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.photo-placeholder')")!=(photo?"true":"false"))throw new IOException("Photo preview variant differs from requested layout");
                int height=(int)Math.Ceiling(double.Parse(await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').getBoundingClientRect().height"),System.Globalization.CultureInfo.InvariantCulture));web.Height=height;await Task.Delay(200);
                string name=template.Id+(photo?"-photo":"")+".png";
                var folder=await StorageFolder.GetFolderFromPathAsync(output);var file=await folder.CreateFileAsync(name,CreationCollisionOption.ReplaceExisting);
                using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite)) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
                images.Add(new {template=template.Id,photo,height,name,miSansLoaded=true});
            }
            await File.WriteAllTextAsync(Path.Combine(output,"template-assets-result.json"),JsonSerializer.Serialize(new {status="passed",images},Services.BackendClient.Json));
        }
        catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"template-assets-result.json"),JsonSerializer.Serialize(new {status="failed",error=ex.ToString(),images},Services.BackendClient.Json));}
        finally{body.Children.Remove(web);web.Close();}
    }
}
