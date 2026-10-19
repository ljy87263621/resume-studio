using ResumeStudio.Services;
using System.Text.Json;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Content;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
if(!File.Exists(Path.Combine(root,"text.pdf"))){
    string[] lines=["姓名：张三","求职意向：嵌入式软件工程师","电话：13800138000","邮箱：zhangsan@example.com","城市：深圳","教育背景","2023.09 - 2027.06 测试大学 电子信息工程 本科","专业技能","熟悉 C/C++、Linux、FreeRTOS 和 STM32。","项目经历","智能传感器网关 | 2026.03 - 2026.07","负责 UART 通信和 TCP 数据传输。","自我评价","认真负责，具备独立调试与团队协作能力。"];
    var builder=new PdfDocumentBuilder();var font=builder.AddTrueTypeFont(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"client-winui/Assets/Fonts/MiSans-Regular.ttf")));var page=builder.AddPage(PageSize.A4);
    for(int i=0;i<lines.Length;i++)page.AddText(lines[i],13,new PdfPoint(42,790-i*35),font);
    File.WriteAllBytes(Path.Combine(root,"text.pdf"),builder.Build());
    var rendered=await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.Combine(root,"text.pdf")));
    using var first=rendered.GetPage(0);using var stream=new InMemoryRandomAccessStream();await first.RenderToStreamAsync(stream,new Windows.Data.Pdf.PdfPageRenderOptions {DestinationWidth=1488,DestinationHeight=2105});stream.Seek(0);var buffer=new Windows.Storage.Streams.Buffer((uint)stream.Size);await stream.ReadAsync(buffer,(uint)stream.Size,InputStreamOptions.None);CryptographicBuffer.CopyToByteArray(buffer,out byte[] png);File.WriteAllBytes(Path.Combine(root,"scan.png"),png);
    var scanBuilder=new PdfDocumentBuilder();scanBuilder.AddPage(PageSize.A4).AddPng(png,new PdfRectangle(0,0,595,842));File.WriteAllBytes(Path.Combine(root,"scan.pdf"),scanBuilder.Build());
    var mixed=new PdfDocumentBuilder();var mixedFont=mixed.AddTrueTypeFont(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"client-winui/Assets/Fonts/MiSans-Regular.ttf")));var mixedText=mixed.AddPage(PageSize.A4);for(int i=0;i<lines.Length;i++)mixedText.AddText(lines[i],13,new PdfPoint(42,790-i*35),mixedFont);mixed.AddPage(PageSize.A4).AddPng(png,new PdfRectangle(0,0,595,842));File.WriteAllBytes(Path.Combine(root,"mixed.pdf"),mixed.Build());
}
bool textOnly=args.Contains("--text-only");
if(!File.Exists(Path.Combine(root,"columns.pdf"))){var columnPdf=new PdfDocumentBuilder();var columnFont=columnPdf.AddTrueTypeFont(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"client-winui/Assets/Fonts/MiSans-Regular.ttf")));var columnPage=columnPdf.AddPage(PageSize.A4);string[] left=["姓名：张三","求职意向：嵌入式实习","电话：13800138000","邮箱：zhangsan@example.com","城市：深圳","专业技能","C/C++","Linux"],right=["教育背景","测试大学 电子信息工程","项目经历","传感器网关","负责 UART 通信调试","实现 TCP 数据传输"];for(int i=0;i<left.Length;i++)columnPage.AddText(left[i],11,new PdfPoint(34,790-i*35),columnFont);for(int i=0;i<right.Length;i++)columnPage.AddText(right[i],11,new PdfPoint(320,790-i*35),columnFont);File.WriteAllBytes(Path.Combine(root,"columns.pdf"),columnPdf.Build());}
foreach(string name in new[]{"text","scan","mixed","columns"}){
    if(textOnly&&name is not ("text" or "columns"))continue;
    var result=await ResumePdfImport.Read(Path.Combine(root,name+".pdf"));
    if(!result.Text.Contains("13800138000")||!result.Text.Contains("example.com")||!result.Text.Replace(" ","").Contains("项目"))throw new Exception(name+": contact and Chinese sections missing: "+result.Text);
    if(result.OcrPages!=(name is "text" or "columns"?0:1))throw new Exception(name+": wrong OCR page count");
    await File.WriteAllTextAsync(Path.Combine(root,name+"-desktop.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
    Console.WriteLine("PASS: "+name+" PDF extracts Chinese sections and contact details using OCR only where needed");
}
using var cancellation=new CancellationTokenSource();cancellation.Cancel();
try{await ResumePdfImport.Read(Path.Combine(root,"scan.pdf"),cancellation:cancellation.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Console.WriteLine("PASS: cancelled PDF import returns without creating a resume");}
