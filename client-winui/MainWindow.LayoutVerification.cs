using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task VerifyResumeLayout(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        string alpha=await EncodeModuleImage([0,0,255,0,0,255,0,128,255,0,0,255,255,255,255,255],2,2,true);
        string alphaPath=Path.Combine(output,"transparent-fixture.png");await File.WriteAllBytesAsync(alphaPath,Convert.FromBase64String(alpha[(alpha.IndexOf(',')+1)..]));
        string importedAlpha=await ReadModuleImage(alphaPath);Check(importedAlpha.StartsWith("data:image/png;base64,"),"native image import retains transparency instead of introducing a black JPEG background");
        await VerifyOrientedModuleImage(output,Check);
        current=new Resume {Title="排版能力验收",TemplateId="campus",FontSize=11,LineHeight=1.4,PageMarginVertical=12,PageMarginHorizontal=18,SectionSpacing=4,HeaderAlignment="center",
            Profile=new Profile {Name="排版验证",Role="软件工程师",Phone="138 0000 0000",Email="layout@example.com",City="杭州",Items=[new ProfileItem {Emoji="🎓",Label="学历",Value="本科"}]}};
        var image=new ResumeImage {DataUrl=await ReadModuleImage(Path.Combine(AppContext.BaseDirectory,"Assets","AppLogo.png")),Alt="本机图片验证",Caption="作品图片：嵌入到 PDF 和 Word"};
        var section=new ResumeSection {Title="作品与项目",Images=[image],Content="### 项目概览\n"+ResumeLayout.Experience("简历排版工具","界面与导出","2026.09","至今","实现模块化内容编辑\n验证 A4 预览与导出")+"\n"+ResumeLayout.Table([["项目","说明"],["C# | WinUI","可逐格编辑\n保留换行"]])+"\n### 作品图片\n"+ResumeLayout.ImageToken(image)};
        current.Sections=[section,new ResumeSection {Title="隐藏内容",Content="不应导出",Visible=false}];
        current=await api.Save("resumes",current);dirty=false;showPreviewExamples=false;await OpenEditor();await WaitForPreview();
        Check(await preview!.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').textContent.includes('学历：本科') && document.querySelectorAll('figure img').length===1 && document.querySelectorAll('table tbody tr').length===1 && !document.querySelector('article').textContent.includes('不应导出')")=="true","native preview combines custom contacts, directory, decoded image, semantic table and visibility");
        foreach(var template in templates){
            current.TemplateId=template.Id;current.HeaderAlignment="left";current.SectionSpacing=8;await WaitForPreview();
            Check(await preview.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('header')).textAlign==='left' && Math.abs(parseFloat(getComputedStyle(document.querySelector('h2')).marginTop)-8*96/25.4)<.1")=="true","page alignment and section spacing override template defaults: "+template.Id);
        }
        current.TemplateId="campus";current.HeaderAlignment="center";current.SectionSpacing=4;Changed();await SaveCurrent();await WaitForPreview();
        section=current.Sections[0];editorParts!.SelectedIndex=0;await Task.Delay(150);
        var input=FindTextField(body,"内容")!;input.Select(input.Text.IndexOf("| 项目",StringComparison.Ordinal),0);
        string before=section.Content;var editing=InsertContent(input,5);var dialog=await FeatureDialog("编辑表格");await Task.Delay(180);
        FindTextField(dialog,"表头 1")!.Text="取消的改动";dialog.Hide();await editing;
        Check(section.Content==before,"canceling native cell editor leaves original Markdown unchanged");
        input=FindTextField(body,"内容")!;input.Select(input.Text.IndexOf("| 项目",StringComparison.Ordinal),0);
        editing=InsertContent(input,5);dialog=await FeatureDialog("编辑表格");await Task.Delay(180);FindTextField(dialog,"表头 1")!.Text="验收表头";
        await FeatureClick(dialog,"插入 / 应用");await editing;await SaveCurrent();await WaitForPreview();
        Check(section.Content.Contains("| 验收表头 |")&&await preview.CoreWebView2.ExecuteScriptAsync("document.querySelector('th').textContent === '验收表头'")=="true","native table editor applies edited cell and refreshes saved preview");
        var saved=(await api.Get<List<Resume>>("resumes")).Single(r=>r.Id==current.Id);
        Check(saved.PageMarginVertical==12&&saved.HeaderAlignment=="center"&&saved.Profile.Items.Single().Value=="本科"&&saved.Sections[0].Images.Single().DataUrl==image.DataUrl,"layout settings, contacts and image bytes persist in backend");
        double originalWidth=AppWindow.Size.Width,originalHeight=AppWindow.Size.Height;
        double scale=shell.XamlRoot.RasterizationScale;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1500*scale),(int)(1000*scale)));await Task.Delay(400);await UpdatePreviewScale();
        Check(editorWorkspace!.ActualWidth>=1100&&editorFields!.Visibility==Visibility.Visible&&editorPreviewFrame!.Visibility==Visibility.Visible&&Grid.GetColumn(editorFields)==4,"wide window shows directory, centered A4 preview and right editor");
        await SaveNativePreview(Path.Combine(output,"layout-wide.png"));
        string capacity=capacityText!.Text;var web=preview;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(900*scale),(int)(800*scale)));await Task.Delay(350);editorView!.SelectedIndex=1;await Task.Delay(150);await UpdatePreviewScale();await UpdateCapacity(web!);
        Check(editorFields!.Visibility==Visibility.Collapsed&&editorPreviewFrame!.Visibility==Visibility.Visible&&preview==web,"narrow view selector preserves native WebView and exposes full preview");
        Check(capacity==capacityText.Text,"A4 capacity remains stable when only preview scale changes");
        Check(await preview.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').offsetWidth===794 && document.documentElement.scrollWidth<=innerWidth+2")=="true","narrow preview keeps A4 layout width without horizontal overflow");
        await SaveNativePreview(Path.Combine(output,"layout-narrow-preview.png"));editorView.SelectedIndex=0;await Task.Delay(180);
        Check(editorFields.Visibility==Visibility.Visible&&editorPreviewFrame!.Visibility==Visibility.Collapsed,"narrow window switches back to usable editing controls");
        await SaveNativePreview(Path.Combine(output,"layout-narrow-editor.png"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)originalWidth,(int)originalHeight));await Task.Delay(250);
        await PrintPdf(Path.Combine(output,"layout-rich-content.pdf"));
        var pages=await PdfInspection.Read(Path.Combine(output,"layout-rich-content.pdf"));Check(pages.Count==1&&!pages[0].NearlyBlank,"compact default image and rich content fit on one nonblank A4 page");
        var docx=await api.Post<JsonElement>("export/docx",new {id=current.Id});await File.WriteAllBytesAsync(Path.Combine(output,"layout-rich-content.docx"),Convert.FromBase64String(docx.GetProperty("base64").GetString()!));
        string jsonPath=Path.Combine(output,"layout-roundtrip.json");await ExportResumeFile(jsonPath);await ImportResumeFile(jsonPath);await WaitForPreview();
        Check(current.Profile.Items.Count==1&&current.Sections[0].Images.Count==1&&current.PageMarginHorizontal==18,"exported JSON imports as a new resume retaining rich content and page settings");
        preferences.Theme="dark";await ApplyPreferences();await WaitForPreview();await SaveNativePreview(Path.Combine(output,"layout-dark.png"));
    }
    private static async Task VerifyOrientedModuleImage(string output,Action<bool,string> check)
    {
        // Four quadrants expose row-stride errors that solid color bands can hide.
        var pixels=new byte[80*40*4];for(int y=0;y<40;y++)for(int x=0;x<80;x++){int i=(y*80+x)*4;pixels[i]=(byte)(x>=40&&y<20?255:0);pixels[i+1]=(byte)(y>=20?255:0);pixels[i+2]=(byte)(x<40&&y<20||x>=40&&y>=20?255:0);pixels[i+3]=255;}
        string source=await EncodeModuleImage(pixels,80,40,false);byte[] jpeg=Convert.FromBase64String(source[(source.IndexOf(',')+1)..]);
        using var stream=new MemoryStream();stream.Write(jpeg,0,2);
        stream.Write(Convert.FromHexString("FFE1002245786966000049492A0008000000010012010300010000000600000000000000"));stream.Write(jpeg,2,jpeg.Length-2);
        string path=Path.Combine(output,"oriented-fixture.jpg");await File.WriteAllBytesAsync(path,stream.ToArray());
        string imported=await ReadModuleImage(path);byte[] result=Convert.FromBase64String(imported[(imported.IndexOf(',')+1)..]);
        using var decodedStream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(decodedStream)){writer.WriteBytes(result);await writer.StoreAsync();writer.DetachStream();}decodedStream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(decodedStream);var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new BitmapTransform(),ExifOrientationMode.RespectExifOrientation,ColorManagementMode.DoNotColorManage);var actual=data.DetachPixelData();
        bool Color(int x,int y,int b,int g,int r){int offset=(y*40+x)*4;return Math.Abs(actual[offset]-b)<45&&Math.Abs(actual[offset+1]-g)<45&&Math.Abs(actual[offset+2]-r)<45;}
        check(decoder.PixelWidth==40&&decoder.PixelHeight==80&&Color(10,20,0,255,0)&&Color(30,20,0,0,255)&&Color(10,60,0,255,255)&&Color(30,60,255,0,0),"EXIF-rotated non-square JPEG retains correct row stride and color placement");
    }
}
