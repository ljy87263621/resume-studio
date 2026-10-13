using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task ImportExternalFavorite()
    {
        string clipboard="";
        var clipboardData=Clipboard.GetContent();
        if(clipboardData.Contains(StandardDataFormats.Text)) clipboard=await clipboardData.GetTextAsync();
        var content=Stack(14); content.Children.Add(Text("粘贴职位详情链接后会尝试自动读取公开信息；BOSS 等页面需要正常浏览时，可选择“浏览并读取”。也支持粘贴完整分享文本。",14));
        var input=new TextBox { Header="职位链接 / 分享文本",AcceptsReturn=true,Text=clipboard.Contains("https://")?clipboard:"",TextWrapping=TextWrapping.Wrap,MinHeight=160,MaxHeight=300,MaxLength=50000 }; content.Children.Add(input);
        content.Children.Add(Text("收藏保存到拾简。平台账号里的收藏需要在原站操作。",12));
        var pasted=await new ContentDialog { XamlRoot=shell.XamlRoot,Title="从链接收藏职位",Content=content,PrimaryButtonText="自动解析",CloseButtonText="取消" }.ShowAsync();
        if(pasted!=ContentDialogResult.Primary) return;
        var captured=await ResolveJobLink(input.Text);
        await EditFavorite(captured.Job,true,captured.Message);
    }
    private async Task EditFavorite(Job job,bool create,string message="")
    {
        if(!create&&!(await api.Get<List<Job>>("favorites")).Any(j=>j.Id==job.Id))throw new InvalidOperationException("收藏已被移除，请刷新列表。");
        job=CloneDocument(job);
        var content=Stack(12); if(!string.IsNullOrEmpty(message)) content.Children.Add(Text(message,12));
        var title=new TextBox { Header="职位名称",Text=job.Title,MaxLength=250 }; var company=new TextBox { Header="公司",Text=job.Company,MaxLength=200 }; var location=new TextBox { Header="城市 / 地点",Text=job.Location,MaxLength=200 }; var salary=new TextBox { Header="薪资",Text=job.Salary,MaxLength=100 }; var url=new TextBox { Header="职位详情链接",Text=job.Url,MaxLength=3000 }; var description=new TextBox { Header="职位描述（可从原站复制）",AcceptsReturn=true,Text=job.Description,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxLength=30000 }; var notes=new TextBox { Header="备注",AcceptsReturn=true,Text=job.Notes,TextWrapping=TextWrapping.Wrap,MinHeight=60,MaxLength=5000 };
        foreach(var field in new[] {title,company,location,salary,url,description,notes}) content.Children.Add(field);
        bool created=true;
        var dialog=SaveForm(create?"核对并收藏职位":"编辑收藏",content,"保存收藏",async ()=> {
        if(string.IsNullOrWhiteSpace(title.Text)) throw new InvalidOperationException("请填写职位名称。");JobWorkflow.ValidateLink(url.Text);
        var normalized=await api.Post<CapturePreview>("capture-preview",new {text=url.Text});
        job.Title=title.Text.Trim(); job.Company=company.Text.Trim(); job.Location=location.Text.Trim(); job.Salary=salary.Text.Trim(); job.Url=normalized.Job.Url; job.CanonicalUrl=normalized.Job.CanonicalUrl; job.Description=description.Text; job.Notes=notes.Text;
        // Only external links use the canonical URL identifier; live provider IDs remain unchanged.
        if(create)job.Id=normalized.Job.Id;
        if(create||job.Id.StartsWith("external-"))job.Source=normalized.Job.Source;
        job.InfoStatus="用户核对";
        if(create){var result=await api.Post<CollectionResult<Job>>("favorites/collect",job);created=result.Created;job=result.Record;}
        else {job=await api.Save("favorites",job);}
        });
        dialog.SecondaryButtonText="浏览并读取";dialog.IsSecondaryButtonEnabled=true;
        while(true) {
            var choice=await dialog.ShowAsync();
            if(choice==ContentDialogResult.None)return;
            if(choice==ContentDialogResult.Primary)break;
            job.Url=url.Text;job.Title=title.Text;job.Company=company.Text;job.Location=location.Text;job.Salary=salary.Text;job.Description=description.Text;job.Notes=notes.Text;
            try {var read=await ReadVisibleJob(job);if(read is not null){job=read;url.Text=job.Url;title.Text=job.Title;company.Text=job.Company;location.Text=job.Location;salary.Text=job.Salary;description.Text=job.Description;}}
            catch(Exception ex) {Feedback("页面读取未完成",ex.Message);}
        }
        if(create){favoriteQuery="";favoriteSource="全部平台";}
        await Navigate("favorites");Success(create?(created?"职位已收藏":"已有该职位的收藏"):"收藏已保存",created?job.Title:"已保留原有备注和内容；如需修改，请选择“编辑收藏”。");
    }
    private async Task CollectFavorite(Job job)
    {
        var result=await api.Post<CollectionResult<Job>>("favorites/collect",job);
        Success(result.Created?"职位已收藏":"已有该职位的收藏",result.Created?job.Title:"原有备注和内容已保留，可在职位收藏中编辑。");
    }
    private async Task RemoveFavorite(Job job)
    {
        var saved=(await api.Get<List<Job>>("favorites")).FirstOrDefault(j=>j.Id==job.Id);
        if(saved is null)return;
        await api.Delete("favorites/"+job.Id);await RefreshPage("favorites");
        Feedback("已取消收藏",saved.Title,async ()=>{await api.Post<CollectionResult<Job>>("favorites/collect",saved);await RefreshPage("favorites");});
    }
    private HyperlinkButton JobLink(Job job)
    {
        var text=Text(job.Url,12); text.MaxLines=2; text.TextTrimming=TextTrimming.CharacterEllipsis; text.Foreground=Brush(darkStyle?"#B5C9FF":Accent);
        var link=new HyperlinkButton {Content=text,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,IsEnabled=!string.IsNullOrWhiteSpace(job.Url)};
        ToolTipService.SetToolTip(link,job.Url); link.Click+=async (_,_)=>await Run(()=>Launch(job.Url)); return link;
    }
    private Task CopyJobLink(Job job)
    {
        var data=new DataPackage(); data.SetText(job.Url); Clipboard.SetContent(data); Success("链接已复制","已复制保留访问参数的完整职位链接。"); return Task.CompletedTask;
    }
    private async Task<CapturePreview> ResolveJobLink(string text)
    {
        notice.Title="正在读取职位详情"; notice.Message="尝试解析公开详情页…"; notice.Severity=InfoBarSeverity.Informational; notice.IsOpen=true;
        try { return await api.Post<CapturePreview>("capture-preview",new {text,resolve=true}); }
        finally { notice.IsOpen=false; }
    }
    private async Task ResolveFavorite(Job job)
    {
        var result=await ResolveJobLink(job.Url);
        var updated=CloneDocument(job);
        if(result.Status=="parsed")
        {
            var r=result.Job;
            if(!string.IsNullOrWhiteSpace(r.Title)) updated.Title=r.Title;
            if(!string.IsNullOrWhiteSpace(r.Company)) updated.Company=r.Company;
            if(!string.IsNullOrWhiteSpace(r.Location)) updated.Location=r.Location;
            if(!string.IsNullOrWhiteSpace(r.Salary)) updated.Salary=r.Salary;
            if(!string.IsNullOrWhiteSpace(r.Description)) updated.Description=r.Description;
        }
        await EditFavorite(updated,false,result.Message);
    }
}
