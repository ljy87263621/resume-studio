using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly List<string> iterationChecks=[];
    private static string NormalizeLines(string value)=>value.Replace("\r\n","\n").Replace('\r','\n');
    private async Task IterationOnlyTest()
    {
        Title="拾简 · 界面自动验证窗口"; shell.IsHitTestVisible=false;
        var output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"iteration"); Directory.CreateDirectory(output);
        try
        {
            await Navigate("jobs");
            shownCompanyJobs=new JobResult { Items=[new Job { Title="隔离 UI 测试数据",Company="测试公司",Url="https://example.com/jobs/test",Source="测试数据" }] };
            await VerifyIteration(output);
        }
        catch(Exception e) { await File.WriteAllTextAsync(Path.Combine(output,"iteration-result.json"),JsonSerializer.Serialize(new {status="failed",error=e.ToString()},BackendClient.Json)); await SaveNativePreview(Path.Combine(output,"failed-state.png")); }
        finally { shell.IsHitTestVisible=true; }
    }
    private static ComboBox? FindCombo(DependencyObject parent,string header)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is ComboBox combo&&combo.Header?.ToString()==header) return combo;
            var found=FindCombo(child,header); if(found is not null) return found;
        }
        return null;
    }
    private async Task VerifyIteration(string output)
    {
        if(visibleJobList is null||shownCompanyJobs is null) throw new Exception("Job list verification missing");
        var realJobs=shownCompanyJobs.Items;
        var fixtures=Enumerable.Range(0,1000).Select(i=>new Job { Id="virtual-"+i,Title="隔离虚拟列表验证 "+i,Company="测试公司",Url="https://example.com/jobs/"+i,Source="测试数据",Description=new string('字',200) }).ToList();
        visibleJobList.ItemsSource=fixtures; await Task.Delay(300);
        if(visibleJobList.ContainerFromIndex(999) is not null) throw new Exception("Long list was fully realized");
        visibleJobList.ScrollIntoView(fixtures[^1]); await Task.Delay(350);
        if(visibleJobList.ContainerFromIndex(999) is null) throw new Exception("Virtual list cannot scroll to last item");
        visibleJobList.ItemsSource=realJobs; iterationChecks.Add("1000-job virtualized list and scrolling");

        current=await api.Save("resumes",Demo(templates[0])); dirty=false; await OpenEditor();
        var originalWeb=preview; editorParts!.SelectedIndex=2; await Task.Delay(150);
        var field=FindTextField(body,"内容")??throw new Exception("Module editor field missing");
        field.Text="## 模块导航验证 | 2026\n- 最新内容必须保存并进入预览。";
        await Task.Delay(100); await SaveCurrent(); await WaitForPreview();
        var saved=(await api.Get<List<Resume>>("resumes")).First(r=>r.Id==current.Id);
        if(saved.Sections[0].Content!=NormalizeLines(field.Text)) throw new Exception("Module content was not saved");
        if(await preview!.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').textContent.includes('最新内容必须保存')")!="true") throw new Exception("Preview does not contain latest module edit");
        editorParts.SelectedIndex=0; await Task.Delay(50); editorParts.SelectedIndex=2; await Task.Delay(150);
        if(NormalizeLines(FindTextField(body,"内容")?.Text??"")!=saved.Sections[0].Content||preview!=originalWeb) throw new Exception("Module selection lost state: "+JsonSerializer.Serialize(new {selectedEditorPart,selected=editorParts.SelectedIndex,ui=FindTextField(body,"内容")?.Text,saved=saved.Sections[0].Content,model=current.Sections[0].Content,webReused=preview==originalWeb}));
        iterationChecks.Add("module navigation, native edit, autosave and current preview");

        await Navigate("settings"); await Task.Delay(100);
        var selector=FindCombo(body,"界面版本")??throw new Exception("Settings selector missing"); selector.SelectedIndex=1;
        for(int i=0;i<100&&(modernNavigation is null||FindCombo(body,"界面版本")?.SelectedIndex!=1);i++) await Task.Delay(50);
        if(modernNavigation is null||AppPreferences.Load(api.DataDirectory).InterfaceStyle!="haru") throw new Exception("Native appearance selection not persisted");
        iterationChecks.Add("native interface selector and settings persistence");
        preferences.Theme="light"; await ApplyPreferences(); await Navigate("templates"); await SaveNativePreview(Path.Combine(output,"haru-templates-light.png"));
        await OpenEditor(); editorParts!.SelectedIndex=2; await SaveNativePreview(Path.Combine(output,"haru-editor-light.png"));
        preferences.Theme="dark"; await ApplyPreferences(); await SaveNativePreview(Path.Combine(output,"haru-editor-dark.png"));
        if(shell.RequestedTheme!=ElementTheme.Dark||preview!=originalWeb) throw new Exception("Dark theme or WebView reuse failed");
        iterationChecks.Add("Haru light and dark themes without WebView recreation");
        await PrintPdf(Path.Combine(output,"module-edited-dark-theme.pdf"));
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(output);
        var image=await folder.CreateFileAsync("browser-preview-dark.png",Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using(var stream=await image.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite)) await preview!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,stream);

        preferences.LivePreview=false; await preferences.SaveAsync(api.DataDirectory); editorParts.SelectedIndex=2; await Task.Delay(150);
        var content=FindTextField(body,"内容")!; content.Text+="\n- 关闭实时预览后，PDF 仍必须同步。"; await Task.Delay(100); await SaveCurrent();
        await PrintPdf(Path.Combine(output,"manual-preview-latest.pdf"));
        if((await preview!.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').textContent.includes('PDF 仍必须同步')"))!="true") throw new Exception("Manual preview export stale");
        iterationChecks.Add("manual preview preserves autosave and fresh PDF export");

        preferences.InterfaceStyle="classic"; preferences.LivePreview=true; await ApplyPreferences(); await Navigate("templates"); await SaveNativePreview(Path.Combine(output,"classic-templates.png"));
        if(haruStyle||AppPreferences.Load(api.DataDirectory).InterfaceStyle!="classic") throw new Exception("Classic restoration failed");
        iterationChecks.Add("classic interface restoration with shared data");
        await File.WriteAllTextAsync(Path.Combine(output,"iteration-result.json"),JsonSerializer.Serialize(new {status="passed",checks=iterationChecks,startup=timings,webInitializations,previewNavigations,previewPatches},BackendClient.Json));
    }
}
