using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace ResumeStudio.Services;

public sealed record InterviewBlock(string Text,int Level=0);
public sealed record InterviewImportDocument(string SourceName,IReadOnlyList<InterviewBlock> Blocks)
{
    public int[] HeadingLevels=>Blocks.Where(b=>b.Level>0&&!string.IsNullOrWhiteSpace(b.Text)).Select(b=>b.Level).Distinct().Order().ToArray();
}

/// <summary>Reads text only; document macros, images and original files are not retained.</summary>
public static partial class InterviewImport
{
    public const int MaxTextLength=500000;
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    [GeneratedRegex(@"^\s*(#{1,6})\s+(.+?)\s*#*\s*$")] private static partial Regex MarkdownHeading();
    [GeneratedRegex(@"^\s*(?:\d{1,3}(?:\.\d{1,3})*[.、．)）:]\s*|[（(]\d{1,3}[)）]\s*|Q\s*\d+\s*[:：.、-]?\s*|第[一二三四五六七八九十百\d]+题\s*[:：、]?\s*).+",RegexOptions.IgnoreCase)] private static partial Regex NumberedQuestion();
    [GeneratedRegex(@"(?:heading|标题)\s*([1-6])",RegexOptions.IgnoreCase)] private static partial Regex HeadingStyle();
    private static bool AnswerHeading(string text)=>Regex.IsMatch(text.Trim().TrimEnd(':','：'),@"^(?:答案|解答|解析|答题要点|注意事项|示例|代码示例|补充|参考答案|answer|explanation)$",RegexOptions.IgnoreCase);
    private static bool QuestionTitle(string text)=>text.Length<=300&&(NumberedQuestion().IsMatch(text)||text.TrimEnd().EndsWith('？')||text.TrimEnd().EndsWith('?'));
    public static InterviewImportDocument Read(string path)
    {
        var file=new FileInfo(path);if(!file.Exists)throw new IOException("导入文件不存在。");
        if(file.Length>20*1024*1024)throw new IOException("文档不能超过 20 MiB，请拆分后导入。");
        string extension=file.Extension.ToLowerInvariant();
        var blocks=extension switch {".md" or ".markdown" or ".txt"=>ParseText(ReadText(path)),".docx"=>ReadDocx(path),".pdf"=>ReadPdf(path),_=>throw new IOException("请导入 Markdown、TXT、DOCX 或 PDF 文档。")};
        if(blocks.Sum(b=>b.Text.Length)>MaxTextLength)throw new IOException("文档文字超过 50 万字符，请拆分后导入。");
        if(!blocks.Any(b=>!string.IsNullOrWhiteSpace(b.Text)))throw new IOException(extension==".pdf"?"PDF 中没有可提取的文字，可能是扫描件。请先用 OCR 转成文字文档，再导入。":"文档中没有可导入的文字。");
        return new(file.Name,blocks);
    }
    private static string ReadText(string path)
    {
        byte[] bytes=File.ReadAllBytes(path);
        if(bytes.Length>=2&&((bytes[0]==255&&bytes[1]==254)||(bytes[0]==254&&bytes[1]==255)))return File.ReadAllText(path);
        try{return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}
        catch(DecoderFallbackException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);return Encoding.GetEncoding(54936).GetString(bytes);}
    }
    public static List<InterviewBlock> ParseText(string text)
    {
        if(text.Length>MaxTextLength)throw new IOException("文档文字超过 50 万字符，请拆分后导入。");
        var result=new List<InterviewBlock>();string? fence=null;
        foreach(string raw in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')){
            string line=raw.TrimEnd(),trim=line.TrimStart();
            if(trim.StartsWith("```",StringComparison.Ordinal)||trim.StartsWith("~~~",StringComparison.Ordinal)){string marker=trim[..3];if(fence is null)fence=marker;else if(fence==marker)fence=null;result.Add(new(line));continue;}
            if(fence is not null){result.Add(new(line));continue;}
            var match=MarkdownHeading().Match(line);
            if(match.Success){result.Add(new(match.Groups[2].Value,match.Groups[1].Length));continue;}
            if(Regex.IsMatch(trim,@"^(?:={3,}|-{3,})$")&&result.Count>0&&result[^1].Level==0&&!string.IsNullOrWhiteSpace(result[^1].Text)){
                result[^1]=result[^1] with {Level=trim[0]=='='?1:2};continue;
            }
            result.Add(new(line));
        }
        // Explicit document headings take precedence over numbered answer lists.
        if(!result.Any(b=>b.Level>0))for(int i=0;i<result.Count;i++){
            var b=result[i];bool separated=i==0||string.IsNullOrWhiteSpace(result[i-1].Text);
            if((NumberedQuestion().IsMatch(b.Text)&&b.Text.Length<=300)||(separated&&QuestionTitle(b.Text)))result[i]=b with {Level=1};
        }
        return result;
    }
    public static int AutoLevel(InterviewImportDocument document)
    {
        return document.Blocks.Where(b=>b.Level>0&&!string.IsNullOrWhiteSpace(b.Text)&&!AnswerHeading(b.Text)).GroupBy(b=>b.Level)
            .OrderByDescending(g=>g.Count(b=>QuestionTitle(b.Text))).ThenByDescending(g=>g.Count()).ThenBy(g=>g.Key).Select(g=>g.Key).FirstOrDefault();
    }
    public static List<InterviewQuestion> Split(InterviewImportDocument document,int level=0)
    {
        if(level==0)level=AutoLevel(document);
        var questions=new List<InterviewQuestion>();InterviewQuestion? current=null;var answer=new StringBuilder();
        void Flush(){if(current is null)return;current.Answer=answer.ToString().Trim();if(current.Answer.Length>50000)throw new IOException("单题正文超过 50000 个字符，请增加题目标题分段。");questions.Add(current);current=null;answer.Clear();}
        foreach(var block in document.Blocks){
            // Word often leaves empty paragraphs with a heading style. They are spacing,
            // not question boundaries, and must not discard the following answer text.
            if(string.IsNullOrWhiteSpace(block.Text)){if(current is not null)answer.AppendLine();continue;}
            if(level>0&&block.Level==level&&!AnswerHeading(block.Text)){
                Flush();string title=block.Text.Trim();if(title.Length>300)throw new IOException("题目标题超过 300 个字符，请整理小标题。");current=new(){Title=title};
            }else if(level>0&&block.Level>0&&block.Level<level){Flush();}
            else if(current is not null){answer.AppendLine(block.Level>0?new string('#',Math.Min(block.Level,6))+" "+block.Text:block.Text);}
        }
        Flush();
        if(level==0){string content=string.Join("\n",document.Blocks.Select(b=>b.Text)).Trim();if(content.Length>50000)throw new IOException("未发现小标题，无法拆分长文档。请添加小标题后导入。");string title=Path.GetFileNameWithoutExtension(document.SourceName).Trim();questions.Add(new(){Title=string.IsNullOrWhiteSpace(title)?"导入的面试题":title,Answer=content});}
        if(questions.Count==0||questions.Count>2000)throw new IOException("该标题层级应识别出 1–2000 道题，请调整拆分层级。");
        if(questions.Any(q=>string.IsNullOrWhiteSpace(q.Title)||q.Title.Length>300))throw new IOException("题目标题须为 1–300 个字符，请整理小标题后导入。");
        if(questions.Sum(q=>q.Title.Length+q.Answer.Length)>MaxTextLength)throw new IOException("拆分后的题库超过 50 万字符，请拆分文档后导入。");
        return questions;
    }
    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        if(entry.Length>20*1024*1024)throw new IOException("Word 文档展开后过大，请拆分后导入。");
        using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024});return XDocument.Load(reader);
    }
    private static List<InterviewBlock> ReadDocx(string path)
    {
        using var zip=ZipFile.OpenRead(path);var entry=zip.GetEntry("word/document.xml")??throw new IOException("DOCX 文档结构无效。");
        var styles=new Dictionary<string,XElement>();if(zip.GetEntry("word/styles.xml") is {} styleEntry)foreach(var style in LoadXml(styleEntry).Descendants(W+"style"))if(style.Attribute(W+"styleId") is {} id)styles[id.Value]=style;
        int StyleLevel(string? id,HashSet<string> seen){if(id is null||!seen.Add(id)||!styles.TryGetValue(id,out var style))return 0;var outline=style.Element(W+"pPr")?.Element(W+"outlineLvl")?.Attribute(W+"val")?.Value;if(int.TryParse(outline,out int n)&&n is >=0 and <=5)return n+1;var match=HeadingStyle().Match(id+" "+style.Element(W+"name")?.Attribute(W+"val")?.Value);if(match.Success)return int.Parse(match.Groups[1].Value);return StyleLevel(style.Element(W+"basedOn")?.Attribute(W+"val")?.Value,seen);}
        var blocks=new List<InterviewBlock>();int length=0;
        foreach(var paragraph in LoadXml(entry).Descendants(W+"p")){
            string text=string.Concat(paragraph.Descendants().Where(e=>e.Name==W+"t"||e.Name==W+"tab"||e.Name==W+"br").Select(e=>e.Name==W+"t"?e.Value:e.Name==W+"tab"?"\t":"\n"));
            length+=text.Length;if(length>MaxTextLength)throw new IOException("Word 文档文字超过 50 万字符。");
            var properties=paragraph.Element(W+"pPr");int level=StyleLevel(properties?.Element(W+"pStyle")?.Attribute(W+"val")?.Value,[]);
            if(int.TryParse(properties?.Element(W+"outlineLvl")?.Attribute(W+"val")?.Value,out int direct)&&direct is >=0 and <=5)level=direct+1;
            blocks.Add(new(text,string.IsNullOrWhiteSpace(text)?0:level));
        }
        if(!blocks.Any(b=>b.Level>0))return ParseText(string.Join("\n",blocks.Select(b=>b.Text)));
        return blocks;
    }
    private static List<InterviewBlock> ReadPdf(string path)
    {
        using var pdf=PdfDocument.Open(path);if(pdf.NumberOfPages>200)throw new IOException("PDF 超过 200 页，请拆分题库后导入。");
        var sizes=new Dictionary<double,int>();int characters=0;
        foreach(var page in pdf.GetPages())foreach(var letter in page.Letters){
            characters+=letter.Value.Length;if(characters>MaxTextLength)throw new IOException("PDF 文字超过 50 万字符，请拆分后导入。");
            if(!string.IsNullOrWhiteSpace(letter.Value)){double point=Math.Round(letter.PointSize,1);sizes[point]=sizes.GetValueOrDefault(point)+letter.Value.Length;}
        }
        double bodySize=sizes.Count==0?12:sizes.MaxBy(pair=>pair.Value).Key;var headings=sizes.Keys.Where(s=>s>bodySize*1.1).OrderDescending().Take(6).ToArray();
        var result=new List<InterviewBlock>();int length=0;
        foreach(var page in pdf.GetPages()){
            // Use text baselines rather than glyph rectangles: some CJK fonts have oversized
            // glyph bounds that make layout analysis merge a heading with the next answer line.
            var lines=page.Letters.GroupBy(l=>Math.Round(l.Location.Y,1)).OrderByDescending(row=>row.Key);
            foreach(var row in lines){
                var letters=row.OrderBy(l=>l.Location.X).ToList();var line=new StringBuilder();
                for(int i=0;i<letters.Count;i++){
                    var letter=letters[i];if(i>0&&!string.IsNullOrWhiteSpace(letter.Value)&&!string.IsNullOrWhiteSpace(letters[i-1].Value)&&letter.BoundingBox.Left-letters[i-1].BoundingBox.Right>letter.PointSize*.2)line.Append(' ');
                    line.Append(letter.Value);
                }
                string text=line.ToString().Trim();length+=text.Length;if(length>MaxTextLength)throw new IOException("PDF 文字超过 50 万字符，请拆分后导入。");
                double size=letters.Where(l=>!string.IsNullOrWhiteSpace(l.Value)).Select(l=>Math.Round(l.PointSize,1)).DefaultIfEmpty(bodySize).Average();
                int level=text.Length<=300&&size>bodySize*1.1?Math.Clamp(Array.FindIndex(headings,s=>Math.Abs(s-size)<.6)+1,1,6):0;
                if(level==0&&QuestionTitle(text))level=Math.Min(6,headings.Length+1);
                result.Add(new(text,level));
            }
            result.Add(new(""));
        }
        return result;
    }
}
