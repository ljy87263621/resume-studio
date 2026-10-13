using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task WorkbenchTest()
    {
        var output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"workbench");Directory.CreateDirectory(output);
        var checks=new List<string>();
        void Check(bool condition,string label){if(!condition)throw new Exception(label);checks.Add(label);}
        try {
            var template=templates.Single(t=>t.Id=="gh-embedded-billryan");
            current=await api.Save("resumes",Blank(template));dirty=false;showPreviewExamples=true;await OpenEditor();await WaitForPreview();
            Check(current.Sections.All(s=>s.Content=="")&&current.Profile.Name=="","new resume remains empty despite complete sample preview");
            Check(await preview!.CoreWebView2.ExecuteScriptAsync($"document.querySelectorAll('section').length === {current.Sections.Count} && document.querySelector('article').textContent.includes('示例大学')")=="true","blank workbench renders every template module with full sample content");
            Check(editorParts!.CanDragItems&&editorParts.CanReorderItems&&editorParts.AllowDrop,"native module directory enables drag and automatic reordering");
            Check(InteractionElement<CommandBar>(body,bar=>bar.PrimaryCommands.Contains(layoutButton)) is not null,"layout settings reside in the top command bar");
            layoutButton!.Flyout.ShowAt(layoutButton);await Task.Delay(200);
            var layout=((Flyout)layoutButton.Flyout).Content;
            var font=FindCombo(layout,"简历字体")??throw new Exception("Top layout font selector missing");font.SelectedIndex=2;await SaveCurrent();await WaitForPreview();
            Check(current.FontFamily=="SimSun"&&(await preview.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('article')).fontFamily")).Contains("SimSun"),"top layout font selection changes the live preview and saved resume");
            font.SelectedIndex=0;layoutButton.Flyout.Hide();await Task.Delay(120);
            FindTextField(body,"姓名")!.Text="真实姓名标记";editorParts.SelectedIndex=1;await Task.Delay(120);
            var edited=current.Sections[1];FindTextField(body,"内容")!.Text="- 真实模块标记：只替换这一模块。";await SaveCurrent();await WaitForPreview();
            Check(await preview.CoreWebView2.ExecuteScriptAsync("document.querySelector('section[data-resume-index=\"1\"]').textContent.includes('真实模块标记') && !document.querySelector('section[data-resume-index=\"1\"]').textContent.includes('填写') && document.querySelector('article').textContent.includes('示例大学')")=="true","typed content replaces its entire module while untouched modules keep their examples");
            var before=current.Sections.ToList();editorModules.Move(1,0);Check(CommitEditorOrder(edited),"final native drag collection order is committed as one edit");ShowEditorPart();await SaveCurrent();await WaitForPreview();
            Check(current.Sections[0]==edited&&selectedEditorPart==2&&(await api.Get<List<Resume>>("resumes")).Single(r=>r.Id==current.Id).Sections[0].Content==edited.Content,"reordered module stays selected and its new order persists");
            await UndoEditor(false);Check(current.Sections.Select(s=>s.Title).SequenceEqual(before.Select(s=>s.Title)),"undo restores module order");
            await UndoEditor(true);Check(current.Sections[0].Content.Contains("真实模块标记"),"redo restores reordered content");
            string snapshot=EditorSnapshot();await PrintPdf(Path.Combine(output,"actual-content-only.pdf"));
            Check(snapshot==EditorSnapshot()&&current.Sections.Skip(1).All(s=>s.Content==""),"PDF export does not write example content into stored modules");
            await WaitForPreview();Check(await preview.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').textContent.includes('示例大学')")=="true","full example preview is restored after PDF export");
            examplePreviewToggle!.IsOn=false;await WaitForPreview();
            Check(await preview.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('section').length === 1 && !document.querySelector('article').textContent.includes('示例大学')")=="true","actual-content preview hides all unfilled examples");
            examplePreviewToggle.IsOn=true;await WaitForPreview();await SaveNativePreview(Path.Combine(output,"workbench-classic.png"));
            preferences.InterfaceStyle="haru";preferences.Theme="light";await ApplyPreferences();await WaitForPreview();await SaveNativePreview(Path.Combine(output,"workbench-haru.png"));
            layoutButton!.Flyout.ShowAt(layoutButton);await Task.Delay(180);await SaveNativePreview(Path.Combine(output,"workbench-layout.png"));layoutButton.Flyout.Hide();
            var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(output);var image=await folder.CreateFileAsync("workbench-preview.png",Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using(var stream=await image.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))await preview.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,stream);
            await File.WriteAllTextAsync(Path.Combine(output,"workbench-result.json"),JsonSerializer.Serialize(new {status="passed",checks},BackendClient.Json));
        } catch(Exception error) {
            await File.WriteAllTextAsync(Path.Combine(output,"workbench-result.json"),JsonSerializer.Serialize(new {status="failed",checks,error=error.ToString()},BackendClient.Json));
        } finally {await SaveCurrent();closing=true;api.Dispose();Close();}
    }
}
