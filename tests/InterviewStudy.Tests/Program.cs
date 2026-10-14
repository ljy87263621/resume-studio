using ResumeStudio.Services;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Content;

string output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Path.GetTempPath(),"InterviewImport-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
void Reject(Action action,string name){try{action();}catch(Exception ex)when(ex is IOException or System.Xml.XmlException){Check(true,name);return;}throw new Exception(name);}
string md="# C 面试题\n\n## 1. 指针是什么？\n指针保存地址。\n### 答案\n保留解答小标题。\n1. 答案列表不会变成题目。\n\n## 2. 什么是互斥锁？\n保护共享数据。\n";
File.WriteAllText(Path.Combine(output,"interview.md"),md,new UTF8Encoding(false));
var document=InterviewImport.Read(Path.Combine(output,"interview.md"));var questions=InterviewImport.Split(document);
Check(InterviewImport.AutoLevel(document)==2&&questions.Count==2,"Markdown categories and answer subheadings are excluded from question boundaries");
Check(questions[0].Answer.Contains("### 答案")&&questions[0].Answer.Contains("答案列表"),"question answers retain lists and lower headings");
Check(InterviewImport.Split(document,1).Count==1,"manual heading-level selection can merge a category into one card");
string fence=new((char)96,3);
var code=InterviewImport.ParseText("## 1. Code?\n"+fence+"c\n# include\n## still code\n"+fence+"\n## 2. Next?\nAnswer");
Check(InterviewImport.Split(new("code.md",code)).Count==2,"headings inside fenced code are preserved as answer text");
var setext=InterviewImport.ParseText("One question?\n---\nAnswer\nTwo questions?\n---\nAnswer");
Check(InterviewImport.Split(new("setext.md",setext)).Count==2,"setext Markdown headings split correctly");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string plain="1. 中断是什么？\n响应异步事件。\n\n2. 什么是任务？\n调度单位。";
File.WriteAllBytes(Path.Combine(output,"interview.txt"),Encoding.GetEncoding(54936).GetBytes(plain));
Check(InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview.txt"))).Count==2,"Chinese GB18030 TXT imports numbered question titles");
void Docx(string path,string styles,string body){
    using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
    foreach(var entry in new[]{("word/styles.xml",styles),("word/document.xml",body)}){
        using var stream=new StreamWriter(zip.CreateEntry(entry.Item1).Open(),new UTF8Encoding(false));stream.Write(entry.Item2);
    }
}
string ns="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
string styles=$"""<w:styles xmlns:w="{ns}"><w:style w:styleId="Heading1"><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style><w:style w:styleId="Question"><w:pPr><w:outlineLvl w:val="1"/></w:pPr></w:style><w:style w:styleId="Derived"><w:basedOn w:val="Question"/></w:style></w:styles>""";
string body=$"""<w:document xmlns:w="{ns}"><w:body><w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>嵌入式题库</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val="Derived"/></w:pPr><w:r><w:t>1. 什么是栈？</w:t></w:r></w:p><w:p><w:r><w:t>自动管理的数据区。</w:t></w:r></w:p><w:p><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>2. 什么是堆？</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>显式分配与释放。</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>""";
Docx(Path.Combine(output,"interview.docx"),styles,body);
var word=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview.docx")));
Check(word.Count==2&&word[0].Title.Contains("栈")&&word[1].Answer.Contains("显式"),"DOCX custom inherited styles, outline levels and table text import");
var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);var page=builder.AddPage(PageSize.A4);
page.AddText("1. What is a mutex?",16,new PdfPoint(40,740),font);page.AddText("Mutual exclusion protects shared data.",11,new PdfPoint(40,710),font);
page.AddText("2. What is a semaphore?",16,new PdfPoint(40,640),font);page.AddText("A counter limits concurrent access.",11,new PdfPoint(40,610),font);
File.WriteAllBytes(Path.Combine(output,"interview.pdf"),builder.Build());
var pdf=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview.pdf")));
Check(pdf.Count==2&&pdf[0].Answer.Contains("Mutual exclusion")&&pdf[1].Title.Contains("semaphore"),"real PDF typography and reading order produce two question cards");
string fontPath=args.Length>1?args[1]:Path.Combine(Directory.GetCurrentDirectory(),"client-winui","Assets","Fonts","MiSans-Regular.ttf");
var chineseBuilder=new PdfDocumentBuilder();var chineseFont=chineseBuilder.AddTrueTypeFont(File.ReadAllBytes(fontPath));var chinesePage=chineseBuilder.AddPage(PageSize.A4);
chinesePage.AddText("1. 什么是中断？",16,new PdfPoint(40,740),chineseFont);
chinesePage.AddText("中断响应外部或内部的异步事件，通常由硬件或软件触发。",11,new PdfPoint(40,710),chineseFont);
chinesePage.AddText("2. 什么是任务？",16,new PdfPoint(40,640),chineseFont);
chinesePage.AddText("任务是调度器管理的执行单位，具有自己的状态与执行上下文。",11,new PdfPoint(40,610),chineseFont);
File.WriteAllBytes(Path.Combine(output,"interview-chinese.pdf"),chineseBuilder.Build());
var chinese=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview-chinese.pdf")));
if(chinese.Count!=2||!chinese[0].Title.Replace(" ","").Contains("中断")||!chinese[1].Answer.Replace(" ","").Contains("调度器"))Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{blocks=InterviewImport.Read(Path.Combine(output,"interview-chinese.pdf")).Blocks,questions=chinese},new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
Check(chinese.Count==2&&chinese[0].Title.Replace(" ","").Contains("中断")&&chinese[1].Answer.Replace(" ","").Contains("调度器"),"actual Chinese PDF with embedded MiSans extracts headings and answer glyphs");
var emptyPdf=new PdfDocumentBuilder();emptyPdf.AddPage(PageSize.A4);File.WriteAllBytes(Path.Combine(output,"empty.pdf"),emptyPdf.Build());
Reject(()=>InterviewImport.Read(Path.Combine(output,"empty.pdf")),"image-only/empty PDF gives an explicit no-text error");
Docx(Path.Combine(output,"unsafe.docx"),styles,$"""<!DOCTYPE x [<!ENTITY external SYSTEM "file:///nonexistent">]><w:document xmlns:w="{ns}"><w:body><w:p><w:r><w:t>&external;</w:t></w:r></w:p></w:body></w:document>""");
Reject(()=>InterviewImport.Read(Path.Combine(output,"unsafe.docx")),"DOCX external XML entities cannot be read");
Reject(()=>InterviewImport.ParseText(new string('a',InterviewImport.MaxTextLength+1)),"oversized text is rejected before creating cards");
var deck=new InterviewDeck {Questions=questions};questions[0].Status="remembered";questions[1].Status="learning";
Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,1,1,0),"remembered and unmastered counts are separate");
questions[0].Status="learning";questions[0].Status="learning";
Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,0,2,0),"changing or repeating a verdict never double counts");
Check(InterviewProgress.Filter(deck,"remembered").Count()==0&&InterviewProgress.Filter(deck,"learning").Count()==2,"practice filters follow current states");
var wheel=new InterviewWheelNavigator();var now=DateTimeOffset.UtcNow;
Check(wheel.Step(-60,now)==0&&wheel.Step(-60,now)==1&&wheel.Step(-120,now.AddMilliseconds(100))==0&&wheel.Step(120,now.AddSeconds(1))==-1,"wheel accumulates small deltas and suppresses momentum double-switching");
Check(InterviewWheelNavigator.ReadingAnswer(-120,20,100)&&!InterviewWheelNavigator.ReadingAnswer(-120,100,100)&&InterviewWheelNavigator.ReadingAnswer(120,20,100)&&!InterviewWheelNavigator.ReadingAnswer(120,0,100),"long answers scroll before switching at their edges");
Console.WriteLine($"PASS: {checks} interview import and progress checks. Fixtures: {output}");
