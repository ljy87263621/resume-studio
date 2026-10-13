using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ResumeStudio.Services;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private bool inspectingPdf;
    private async Task CheckPdf(bool export)
    {
        if(current is null||inspectingPdf) return; inspectingPdf=true;
        string directory=Path.Combine(api.DataDirectory,"pdf-check"); Directory.CreateDirectory(directory); string path=Path.Combine(directory,Guid.NewGuid()+".pdf");
        try {
            await SaveCurrent(); int checkedRevision=revision; string checkedId=current.Id,checkedTitle=current.Title;
            await PrintPdf(path); var pages=await PdfInspection.Read(path);
            var panel=Stack(12); panel.Children.Add(Text($"实际 PDF 共 {pages.Count} 页",20,true));
            var blank=pages.Where(p=>p.NearlyBlank).Select(p=>p.Number).ToArray(); if(blank.Length>0) panel.Children.Add(Text("疑似空白页："+string.Join("、",blank)+"。请检查分页后再投递。",14,true));
            if(pages.Count>1) panel.Children.Add(Text("请检查跨页标题、项目条目和末页留白；可回到排版设置调整字号与行距。",13));
            string overflow=await preview!.CoreWebView2.ExecuteScriptAsync("(()=>{const a=document.querySelector('article');const b=a.getBoundingClientRect();return [...a.querySelectorAll('p,li,table,h3')].some(e=>{const r=e.getBoundingClientRect();return r.right>b.right+1||r.left<b.left-1||e.scrollWidth>e.clientWidth+2})})()");
            if(overflow=="true") panel.Children.Add(Text("预览中存在可能横向溢出的条目，请特别检查长链接、表格与日期。",13,true));
            foreach(var p in pages.Take(6)) {
                var bitmap=new BitmapImage(); using var stream=new InMemoryRandomAccessStream(); using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) {writer.WriteBytes(p.Image); await writer.StoreAsync(); await writer.FlushAsync(); writer.DetachStream();} stream.Seek(0); await bitmap.SetSourceAsync(stream);
                panel.Children.Add(Text("第 "+p.Number+" 页",14,true)); panel.Children.Add(new Image {Source=bitmap,MaxWidth=420,HorizontalAlignment=HorizontalAlignment.Center});
            }
            if(pages.Count>6) panel.Children.Add(Text("展示前 6 页；页数和空白检测覆盖整份 PDF。",12));
            var choice=await new ContentDialog {XamlRoot=shell.XamlRoot,Title="PDF 导出检查",Content=new ScrollViewer {Content=panel,MaxHeight=590},PrimaryButtonText=export?"确认并导出":"返回编辑",CloseButtonText=export?"返回修改":"关闭"}.ShowAsync();
            if(!export||choice!=ContentDialogResult.Primary) return;
            if(current?.Id!=checkedId||revision!=checkedRevision) throw new InvalidOperationException("检查期间简历有修改，请重新检查后导出。");
            var picker=new FileSavePicker {SuggestedFileName=SafeName(checkedTitle)}; picker.FileTypeChoices.Add("PDF 文档",new List<string> {".pdf"}); InitPicker(picker); var file=await picker.PickSaveFileAsync(); if(file is null) return;
            await File.WriteAllBytesAsync(file.Path,await File.ReadAllBytesAsync(path)); Success("PDF 已导出",$"{file.Path} · {pages.Count} 页");
        }
        finally {inspectingPdf=false; if(File.Exists(path)) File.Delete(path);}
    }
}
