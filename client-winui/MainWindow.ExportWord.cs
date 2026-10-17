using ResumeStudio.Services;
using Windows.Storage.Pickers;
using System.Text.Json;
namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task ExportWord()
    {
        if(current is null)return;await SaveCurrent();string id=current.Id;
        var picker=new FileSavePicker {SuggestedFileName=SafeName(current.Title)};picker.FileTypeChoices.Add("可编辑 Word 文档",new[]{".docx"});InitPicker(picker);var file=await picker.PickSaveFileAsync();if(file is null)return;
        var document=await api.Post<JsonElement>("export/docx",new {id});await File.WriteAllBytesAsync(file.Path,Convert.FromBase64String(document.GetProperty("base64").GetString()!));
        Success("Word 已导出","正文为可编辑段落；采用独立 Word 排版，不保证与 PDF 模板逐像素一致。");
    }
}
