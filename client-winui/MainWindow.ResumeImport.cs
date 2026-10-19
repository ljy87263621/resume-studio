using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using Windows.Storage.Pickers;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task ImportPdfResume()
    {
        await SaveCurrent();var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".pdf");InitPicker(picker);
        var file=await picker.PickSingleFileAsync();if(file is null)return;await ImportPdfResumeFile(file.Path);
    }
    private async Task ImportPdfResumeFile(string path)
    {
        using var cancellation=new CancellationTokenSource();var status=Text("正在读取 PDF；扫描页面会在本机识别。",14);
        var progressDialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title="识别 PDF 简历",Content=Stack(),CloseButtonText="取消"};
        ((StackPanel)progressDialog.Content).Children.Add(new ProgressBar {IsIndeterminate=true});((StackPanel)progressDialog.Content).Children.Add(status);
        bool completed=false;progressDialog.Closing+=(_,_)=>{if(!completed)cancellation.Cancel();};
        var shown=progressDialog.ShowAsync();ResumePdfText document;
        try {document=await ResumePdfImport.Read(path,new Progress<string>(s=>status.Text=s),cancellation.Token);}
        catch(OperationCanceledException){return;}
        finally {completed=true;progressDialog.Hide();await shown;}
        var draft=await api.Post<ResumeImportDraft>("resume-import",new {sourceName=document.SourceName,text=document.Text,pageCount=document.PageCount,ocrPages=document.OcrPages});
        await ReviewPdfResume(draft);
    }
    private async Task ReviewPdfResume(ResumeImportDraft draft)
    {
        var panel=Stack(12);panel.Children.Add(Text($"{draft.SourceName} · {draft.PageCount} 页 · OCR {draft.OcrPages} 页。请核对识别结果；确认后生成新的简历。",13));
        foreach(string warning in draft.Warnings)panel.Children.Add(Text("• "+warning,12));
        var template=new ComboBox {Header="填入模板",ItemsSource=templates,SelectedItem=templates.FirstOrDefault(t=>t.Id=="gh-embedded-billryan")??templates[0],HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(template);
        var title=new TextBox {Header="新简历名称",Text=Path.GetFileNameWithoutExtension(draft.SourceName),MaxLength=200};panel.Children.Add(title);
        TextBox Input(string label,string value,Action<string> changed,bool multiline=false){var field=new TextBox {Header=label,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?100:36,MaxHeight=multiline?250:double.PositiveInfinity,Text=value};field.TextChanged+=(_,_)=>changed(field.Text);panel.Children.Add(field);return field;}
        Input("姓名",draft.Profile.Name,s=>draft.Profile.Name=s);Input("目标岗位",draft.Profile.Role,s=>draft.Profile.Role=s);
        Input("电话",draft.Profile.Phone,s=>draft.Profile.Phone=s);Input("邮箱",draft.Profile.Email,s=>draft.Profile.Email=s);
        Input("城市",draft.Profile.City,s=>draft.Profile.City=s);Input("个人主页",draft.Profile.Link,s=>draft.Profile.Link=s);
        Input("个人简介",draft.Profile.Summary,s=>draft.Profile.Summary=s,true);
        foreach(var section in draft.Sections){Input("模块名称",section.Title,s=>section.Title=s);Input("识别正文",section.Content,s=>section.Content=s,true);}
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title="核对 PDF 简历",Content=new ScrollViewer {Content=panel,MaxHeight=600},PrimaryButtonText="生成新简历",CloseButtonText="取消",DefaultButton=ContentDialogButton.Primary};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var selected=(Template)template.SelectedItem;await SaveCurrent();
        current=await api.Post<Resume>("resume-import/create",new {templateId=selected.Id,title=title.Text,draft});dirty=false;await OpenEditor();Success("PDF 已填入模板","已生成独立简历，可继续调整排版和内容。");
    }
}
