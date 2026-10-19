using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using UglyToad.PdfPig;

namespace ResumeStudio.Services;

public sealed class ResumeImportDraft
{
    public string SourceName {get;set;}="";
    public Profile Profile {get;set;}=new();
    public List<ResumeSection> Sections {get;set;}=[];
    public List<string> Warnings {get;set;}=[];
    public int PageCount {get;set;}
    public int OcrPages {get;set;}
}
public sealed record ResumePdfText(string SourceName,string Text,int PageCount,int OcrPages);

/// <summary>Extracts embedded text first, and renders only scanned pages for Windows' local OCR.</summary>
public static class ResumePdfImport
{
    public static async Task<ResumePdfText> Read(string path,IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        var file=new FileInfo(path);if(!file.Exists||file.Length==0)throw new IOException("PDF 文件不存在或为空。");
        if(file.Length>20*1024*1024)throw new IOException("PDF 不能超过 20 MiB。");
        if(!file.Extension.Equals(".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择 PDF 简历。");
        var imagePages=new HashSet<int>();
        var texts=await Task.Run(()=>{
            using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);
            if(pdf.NumberOfPages>20)throw new IOException("简历 PDF 最多支持 20 页，请拆分后导入。");
            var pages=new List<string>();
            foreach(var page in pdf.GetPages()){
                cancellation.ThrowIfCancellationRequested();
                var rows=page.Letters.GroupBy(l=>Math.Round(l.Location.Y,1)).OrderByDescending(g=>g.Key).Select(g=>(Y:g.Key,Letters:g.OrderBy(l=>l.Location.X).ToList())).ToList();
                var gaps=new List<(double Left,double Right,double Y)>();
                foreach(var row in rows)for(int i=1;i<row.Letters.Count;i++){double left=row.Letters[i-1].BoundingBox.Right,right=row.Letters[i].BoundingBox.Left;if(right-left>page.Width*.08&&left<page.Width*.7&&right>page.Width*.25)gaps.Add((left,right,row.Y));}
                double? gutter=null;double top=0;
                if(gaps.Count>=3){var best=gaps.Select(g=>(X:(g.Left+g.Right)/2,Matches:gaps.Where(other=>other.Left<(g.Left+g.Right)/2&&other.Right>(g.Left+g.Right)/2).ToList())).OrderByDescending(g=>g.Matches.Count).First();if(best.Matches.Count>=Math.Max(3,rows.Count*.25)&&best.Matches.Max(g=>g.Y)-best.Matches.Min(g=>g.Y)>page.Height*.18){gutter=best.X;top=best.Matches.Max(g=>g.Y);}}
                var ordered=gutter is null?rows.Select(r=>r.Letters):rows.Where(r=>r.Y>top+2).Select(r=>r.Letters).Concat(rows.Where(r=>r.Y<=top+2).Select(r=>r.Letters.Where(l=>l.Location.X<gutter.Value).ToList())).Concat(rows.Where(r=>r.Y<=top+2).Select(r=>r.Letters.Where(l=>l.Location.X>=gutter.Value).ToList()));
                var lines=new List<string>();
                foreach(var letters in ordered){var line=new System.Text.StringBuilder();
                    for(int i=0;i<letters.Count;i++){var letter=letters[i];if(i>0&&letter.BoundingBox.Left-letters[i-1].BoundingBox.Right>letter.PointSize*.35)line.Append(' ');line.Append(letter.Value);}lines.Add(line.ToString());}
                string extracted=string.Join("\n",lines);if(extracted.Count(char.IsLetterOrDigit)<350&&page.GetImages().Any(image=>image.BoundingBox.Width*image.BoundingBox.Height>page.Width*page.Height*.5))imagePages.Add(pages.Count);pages.Add(extracted);
            }
            return pages;
        },cancellation);
        Windows.Data.Pdf.PdfDocument? scanned=null;int ocrPages=0;
        try {
            for(int index=0;index<texts.Count;index++){
                cancellation.ThrowIfCancellationRequested();progress?.Report($"正在识别第 {index+1} / {texts.Count} 页…");
                if(texts[index].Count(char.IsLetterOrDigit)>=30&&!imagePages.Contains(index))continue;
                scanned??=await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(file.FullName));
                var engine=OcrEngine.TryCreateFromLanguage(new Language("zh-Hans-CN"))??OcrEngine.TryCreateFromUserProfileLanguages();
                if(engine is null)throw new IOException("Windows 未安装可用的 OCR 语言，请在系统“语言和区域”添加中文或英文语言识别组件后重试。");
                using var page=scanned.GetPage((uint)index);using var stream=new InMemoryRandomAccessStream();
                double scale=Math.Min(2200/page.Size.Width,2600/page.Size.Height);
                await page.RenderToStreamAsync(stream,new PdfPageRenderOptions {DestinationWidth=(uint)(page.Size.Width*scale),DestinationHeight=(uint)(page.Size.Height*scale)});
                stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore);
                var result=await engine.RecognizeAsync(bitmap);string recognized=string.Join("\n",result.Lines.Select(l=>l.Text));
                // The Latin recognizer is more reliable for email addresses embedded in Chinese text.
                var english=OcrEngine.TryCreateFromLanguage(new Language("en-US"));
                if(english is not null){var latin=await english.RecognizeAsync(bitmap);var email=System.Text.RegularExpressions.Regex.Match(latin.Text,@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}");if(email.Success)recognized="邮箱："+email.Value+"\n"+recognized;var phone=System.Text.RegularExpressions.Regex.Match(latin.Text,@"(?<!\d)1[3-9]\d{9}(?!\d)");if(phone.Success)recognized="电话："+phone.Value+"\n"+recognized;}
                texts[index]=string.IsNullOrWhiteSpace(texts[index])?recognized:texts[index]+"\n"+recognized;ocrPages++;
            }
        }catch(OperationCanceledException){throw;}catch(IOException){throw;}catch(Exception ex){throw new IOException("PDF 无法识别，可能已加密、损坏或扫描页面无法渲染。",ex);}
        string text=string.Join("\n\n",texts);if(text.Length>100000)throw new IOException("识别文字超过 10 万字符，请拆分 PDF。");
        if(text.Count(char.IsLetterOrDigit)<10)throw new IOException("未识别到可用文字，请选择更清晰的 PDF。");
        cancellation.ThrowIfCancellationRequested();return new(file.Name,text,texts.Count,ocrPages);
    }
}
