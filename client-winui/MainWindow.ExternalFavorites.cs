using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task ImportExternalFavorite()
    {
        string clipboard="";
        var clipboardData=Clipboard.GetContent();
        if(clipboardData.Contains(StandardDataFormats.Text)) clipboard=await clipboardData.GetTextAsync();
        var content=Stack(14); content.Children.Add(Text("复制 BOSS、猎聘、智联、前程无忧、拉勾、牛客或企业官网的职位详情链接；可同时粘贴职位名称和描述。",14));
        var input=new TextBox { Header="职位链接 / 分享文本",Text=clipboard.Contains("https://")?clipboard:"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=160,MaxHeight=300,MaxLength=50000 }; content.Children.Add(input);
        content.Children.Add(Text("收藏保存到拾简。平台账号里的收藏需要在原站操作。",12));
        var pasted=await new ContentDialog { XamlRoot=shell.XamlRoot,Title="从链接收藏职位",Content=content,PrimaryButtonText="识别链接",CloseButtonText="取消" }.ShowAsync();
        if(pasted!=ContentDialogResult.Primary) return;
        var captured=await api.Post<CapturePreview>("capture-preview",new {text=input.Text});
        await EditFavorite(captured.Job,true,captured.Message);
    }
    private async Task EditFavorite(Job job,bool create,string message="")
    {
        var content=Stack(12); if(!string.IsNullOrEmpty(message)) content.Children.Add(Text(message,12));
        var title=new TextBox { Header="职位名称",Text=job.Title,MaxLength=250 }; var company=new TextBox { Header="公司",Text=job.Company,MaxLength=200 }; var location=new TextBox { Header="城市 / 地点",Text=job.Location,MaxLength=200 }; var salary=new TextBox { Header="薪资",Text=job.Salary,MaxLength=100 }; var url=new TextBox { Header="职位详情链接",Text=job.Url,MaxLength=3000 }; var description=new TextBox { Header="职位描述（可从原站复制）",Text=job.Description,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxLength=30000 }; var notes=new TextBox { Header="备注",Text=job.Notes,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=60,MaxLength=5000 };
        foreach(var field in new[] {title,company,location,salary,url,description,notes}) content.Children.Add(field);
        var dialog=new ContentDialog { XamlRoot=shell.XamlRoot,Title=create?"核对并收藏职位":"编辑收藏",Content=new ScrollViewer { Content=content,MaxHeight=570 },PrimaryButtonText="保存收藏",CloseButtonText="取消" };
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary) return;
        var normalized=await api.Post<CapturePreview>("capture-preview",new {text=url.Text});
        if(string.IsNullOrWhiteSpace(title.Text)) throw new InvalidOperationException("请填写职位名称");
        job.Title=title.Text.Trim(); job.Company=company.Text.Trim(); job.Location=location.Text.Trim(); job.Salary=salary.Text.Trim(); job.Url=normalized.Job.Url; job.Description=description.Text; job.Notes=notes.Text;
        // Only external links use the canonical URL identifier; live provider IDs remain unchanged.
        if(create||job.Id.StartsWith("external-")) { var oldId=job.Id; job.Id=normalized.Job.Id; job.Source=normalized.Job.Source; if(!create&&oldId!=job.Id) { await api.Save("favorites",job); await api.Delete("favorites/"+oldId); } }
        job.InfoStatus="用户核对"; await api.Save("favorites",job); await Navigate("favorites"); Success("职位已收藏",job.Title);
    }
}
