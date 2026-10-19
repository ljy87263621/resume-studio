using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task ResumeImportFeatureTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"pdf-import-test");
        string fixtures=Environment.GetEnvironmentVariable("RESUMESTUDIO_PDF_FIXTURES")??throw new IOException("PDF fixtures missing");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        try {
            foreach(string name in new[]{"text","scan","mixed","columns"}){
                int count=(await api.Get<List<Resume>>("resumes")).Count;
                var importing=ImportPdfResumeFile(Path.Combine(fixtures,name+".pdf"));
                var dialog=await FeatureDialog("核对 PDF 简历");await Task.Delay(150);
                var field=FindTextField(dialog,"姓名")??throw new Exception("Profile review field missing");field.Text="PDF确认测试";
                Check(FindTextField(dialog,"电话")!.Text.Contains("13800138000"),name+": extracted phone is visible before confirmation");
                await FeatureClick(dialog,"生成新简历");await importing;
                Check(current?.Profile.Name=="PDF确认测试"&&current.Sections.Any(s=>s.Content.Contains("UART")),name+": edited profile and project text fill the selected template");
                Check((await api.Get<List<Resume>>("resumes")).Count==count+1,name+": import preserves previous resumes");
            }
            int beforeCancel=(await api.Get<List<Resume>>("resumes")).Count;
            var cancel=ImportPdfResumeFile(Path.Combine(fixtures,"text.pdf"));var cancelDialog=await FeatureDialog("核对 PDF 简历");await FeatureClick(cancelDialog,"取消");await cancel;
            Check((await api.Get<List<Resume>>("resumes")).Count==beforeCancel,"cancelling review does not create or replace a resume");
            string exported=Path.Combine(output,"imported-template.pdf");await PrintPdf(exported);var extracted=await ResumePdfImport.Read(exported);
            Check(extracted.Text.Replace(" ","").Contains("PDF确认测试")&&extracted.Text.Contains("13800138000")&&extracted.Text.Contains("UART"),"filled template exports an actual PDF with the confirmed profile and project");
            await SaveNativePreview(Path.Combine(output,"resume-import-editor.png"));
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{status="passed",checks},BackendClient.Json));
        }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{status="failed",checks,error=ex.ToString()},BackendClient.Json));}
        finally {await SaveCurrent();await DetachPreviewAsync(true);await api.StopAsync();closing=true;api.Dispose();Close();}
    }
}
