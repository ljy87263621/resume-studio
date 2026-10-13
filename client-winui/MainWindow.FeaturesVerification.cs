using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using ResumeStudio.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static Button? FeatureButton(DependencyObject parent,string label)
    {
        for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent,i);
            if(child is Button b&&b.Content?.ToString()==label) return b;
            var found=FeatureButton(child,label); if(found is not null) return found;
        }
        return null;
    }
    private sealed class MockAiHandler : HttpMessageHandler
    {
        public string Body="",Auth="";
        public required string Suggestion {get;init;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Body=await request.Content!.ReadAsStringAsync(token); Auth=request.Headers.Authorization?.ToString()??"";
            return new(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(new {choices=new[] {new {message=new {content=Suggestion}}}}),Encoding.UTF8,"application/json")};
        }
    }
    private async Task<ContentDialog> FeatureDialog(string title)
    {
        ContentDialog? Find(DependencyObject node) {
            if(node is ContentDialog d&&d.Title?.ToString()==title) return d;
            for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);i++) {var found=Find(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,i)); if(found is not null) return found;}
            return null;
        }
        for(int n=0;n<100;n++) {
            foreach(var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot)) if(popup.Child is not null&&Find(popup.Child) is ContentDialog dialog) return dialog;
            await Task.Delay(50);
        }
        throw new Exception("Native dialog missing: "+title);
    }
    private async Task FeatureClick(ContentDialog dialog,string label)
    {
        await Task.Delay(150); var button=FeatureButton(dialog,label)??throw new Exception("Dialog button missing: "+label);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private async Task FeatureTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"features"); Directory.CreateDirectory(output);
        var checks=new List<string>();
        void Check(bool condition,string label) {if(!condition) throw new Exception(label); checks.Add(label);}
        try {
            var backendTemplates=await api.Get<List<Template>>("templates");
            Check(JsonSerializer.Serialize(templates,BackendClient.Json)==JsonSerializer.Serialize(backendTemplates,BackendClient.Json),"packaged template text matches live backend UTF-8 data");
            await Navigate("templates");
            var awesome=templates.Single(t=>t.Id=="gh-back-awesome");
            Check(awesome.Name=="Awesome-CV · 后端简历"&&awesome.Category=="后端开发"&&awesome.Sections.Contains("专业技能")&&!awesome.Description.Contains('?'),"Chinese template names categories descriptions and section titles");
            await SaveNativePreview(Path.Combine(output,"templates-utf8.png"));
            var empty=ResumeEditing.Format("",0,0,"bullet"); Check(empty.Text=="- ","format empty field");
            var multiline=ResumeEditing.Format("first\nsecond\nthird",6,7,"bullet"); Check(multiline.Text=="first\n- second\nthird","format selected lines without changing neighbors");
            var toggle=ResumeEditing.Format("- first\n- second",0,16,"bullet"); Check(toggle.Text=="first\nsecond","toggle list formatting");
            current=await api.Save("resumes",new Resume {Title="隔离功能验证",TemplateId=templates[0].Id,Profile=new Profile {Name="测试姓名",Phone="private-phone",Email="private-email",Role="嵌入式实习"},Sections=[new() {Title="技能",Content="- Python\n- C / Linux"},new() {Title="项目",Content="## 实验 | 2026\n- 实现 TCP 通信并验证。"}]}); dirty=false;
            await OpenEditor(); editorParts!.SelectedIndex=2; await Task.Delay(180); var input=FindTextField(body,"内容")!;
            input.Select(0,8); var button=FeatureButton(body,"加粗")??throw new Exception("format button missing");
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(180);
            Check(current.Sections[0].Content.StartsWith("**- Python**"),"native format button updates model");
            await UndoEditor(false); Check(current.Sections[0].Content=="- Python\n- C / Linux","undo restores pre-format content");
            await UndoEditor(true); Check(current.Sections[0].Content.StartsWith("**- Python**"),"redo persists content");
            await SaveCheckpoint(); var history=await api.Get<List<ResumeRevision>>($"resumes/{current.Id}/history"); Check(history.Count>=1,"history endpoint and checkpoint");
            var restored=await api.Get<ResumeRevision>($"resumes/{current.Id}/history/{history[0].Id}"); Check(restored.Resume?.Sections[0].Content==current.Sections[0].Content,"history retrieval is exact");
            var job=new Job {Title="嵌入式实习",Company="隔离测试公司",Description="熟悉 C、Linux 和 FreeRTOS，能调试 UART。",Url="https://example.com/job"};
            var match=JobFitAdvisor.Analyze(current,job); Check(match.Any(r=>r.Name=="Linux"&&r.Status=="有相关表述")&&match.Any(r=>r.Name=="RTOS"&&r.Status=="待确认"),"local fit shows supported and missing evidence");
            current.Sections[0].Content="- Python\n- C / Linux"; Changed(); await SaveCurrent();
            var local=JobFitAdvisor.LocalSuggestions(current,job); Check(local.Count==1&&local[0].After.StartsWith("- C / Linux"),"local suggestion reorders existing skills");
            var target=JobFitAdvisor.Apply(current,job,local); Check(target.Id!=current.Id&&current.Sections[0].Content.StartsWith("- Python"),"job variant preserves original");
            string sourceId=current.Id; var fitUi=ShowJobFit(job,current); var fitDialog=await FeatureDialog("岗位对照与简历建议");
            await FeatureClick(fitDialog,"生成新的岗位简历"); await fitUi;
            Check(current.Id!=sourceId&&(await api.Get<List<Resume>>("resumes")).Any(r=>r.Id==sourceId),"native fit dialog creates a new resume and preserves source");
            var original=CloneResume(current); string suggestion=JsonSerializer.Serialize(new {suggestions=local},BackendClient.Json);
            using var handler=new MockAiHandler {Suggestion=suggestion}; using var http=new HttpClient(handler);
            var ai=await AiResumeClient.Suggest(new AppPreferences {AiEndpoint="https://example.com/v1/chat/completions",AiModel="fixture-model"},"fixture-key",current,job,CancellationToken.None,http);
            Check(ai.Count==1&&handler.Auth=="Bearer fixture-key"&&!handler.Body.Contains("private-phone")&&!handler.Body.Contains("private-email")&&!handler.Body.Contains("测试姓名"),"AI wire format and excluded contact fields");
            try {AiResumeClient.Parse(JsonSerializer.Serialize(new {suggestions=new[] {new ResumeSuggestion {SectionIndex=0,Before=current.Sections[0].Content,After="性能提升 99%",Reason="测试拒绝"}}},BackendClient.Json),current); throw new Exception("invented metric accepted");} catch(IOException) {checks.Add("AI invented numeric claim rejected");}
            await AiSecret.Save(api.DataDirectory,"fixture-local-secret"); Check(await AiSecret.Read(api.DataDirectory)=="fixture-local-secret"&&!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(api.DataDirectory,"ai-key.protected"))).Contains("fixture-local-secret"),"Windows encrypted key round trip"); AiSecret.Delete(api.DataDirectory);
            var record=await api.Save("applications",new ApplicationRecord {Title=job.Title,Company=job.Company,Url=job.Url,Status="已投递",ResumeTitle=current.Title,ResumeSnapshot=CloneResume(current),DueAt=DateTimeOffset.Now.AddMinutes(-2).ToString("O"),EventKind="笔试",Review="检查指针与线程知识"});
            current.Sections[0].Content="投递后内容修改"; Changed(); await SaveCurrent(); record.Status="笔试中"; record.ResumeSnapshot=CloneResume(current); record=await api.Save("applications",record);
            Check(record.ResumeSnapshot!.Sections[0].Content==original.Sections[0].Content&&record.Timeline.Count>=3,"snapshot remains fixed across source edits and status changes");
            await CheckApplicationReminders(); Check(notice.Severity==InfoBarSeverity.Warning&&notice.Message.Contains("笔试"),"overdue arrangement reminder in running app");
            await Navigate("applications"); await Task.Delay(180); await SaveNativePreview(Path.Combine(output,"applications.png"));
            Check(FeatureButton(body,"查看投递快照") is not null,"native application snapshot action");
            var editing=EditApplication(record); var editDialog=await FeatureDialog("更新投递记录");
            FindCombo(editDialog,"当前进度")!.SelectedItem="面试中"; FindTextField(editDialog,"笔试／面试复盘")!.Text="原生弹窗复盘保存验证";
            await FeatureClick(editDialog,"保存记录"); await editing;
            record=(await api.Get<List<ApplicationRecord>>("applications")).First(r=>r.Id==record.Id);
            Check(record.Status=="面试中"&&record.Review=="原生弹窗复盘保存验证"&&record.ResumeSnapshot!.Sections[0].Content==original.Sections[0].Content,"native application dialog saves stage and review without changing snapshot");
            current=await api.Save("resumes",target); dirty=false; await OpenEditor(); editorParts!.SelectedIndex=2; await Task.Delay(180); await SaveNativePreview(Path.Combine(output,"editor.png"));
            string pdf=Path.Combine(output,"checked-resume.pdf"); await PrintPdf(pdf); var pages=await PdfInspection.Read(pdf); Check(pages.Count>0&&pages.All(p=>!p.NearlyBlank),"actual PDF page inspection and blank detection");
            await File.WriteAllBytesAsync(Path.Combine(output,"pdf-page-1.png"),pages[0].Image);
            var pdfCheck=CheckPdf(false); var pdfDialog=await FeatureDialog("PDF 导出检查"); await FeatureClick(pdfDialog,"返回编辑"); await pdfCheck; Check(!inspectingPdf,"native PDF inspection dialog opens and closes");
            current.Sections[1].Content=string.Join("\n",Enumerable.Range(0,50).Select(i=>"- 多页项目验证 "+i+"：实现协议通信并核对收发结果，检查页面边距与条目换行。")); Changed(); await SaveCurrent(); await PrintPdf(Path.Combine(output,"multipage.pdf"));
            var many=await PdfInspection.Read(Path.Combine(output,"multipage.pdf")); Check(many.Count>1&&many.All(p=>!p.NearlyBlank),"multipage PDF has actual page count and nonblank pages");
            for(int i=0;i<many.Count;i++) await File.WriteAllBytesAsync(Path.Combine(output,$"multipage-{i+1}.png"),many[i].Image);
            await Navigate("settings"); await Task.Delay(180); Check(FindTextField(body,"完整 Chat Completions 接口地址") is not null,"native AI settings fields");
            var channel=FindCombo(body,"更新通道")??throw new Exception("Update selector missing"); channel.SelectedIndex=1; await Task.Delay(250);
            Check(AppPreferences.Load(api.DataDirectory).UpdateChannel=="nightly","native Nightly channel selection persists"); channel.SelectedIndex=0;
            var route=FindCombo(body,"更新连接方式")??throw new Exception("Update connection selector missing"); route.SelectedIndex=2; await Task.Delay(250);
            Check(AppPreferences.Load(api.DataDirectory).UpdateRoute=="proxy","native proxy route selection persists"); route.SelectedIndex=0;
            Check(FeatureButton(body,"检查更新") is not null&&FeatureButton(body,"下载并安装") is not null,"native release update actions");
            await SaveNativePreview(Path.Combine(output,"settings.png"));
            await VerifyNewFeatures(output,checks);
            await File.WriteAllTextAsync(Path.Combine(output,"feature-result.json"),JsonSerializer.Serialize(new {status="passed",checks,pages=pages.Count,multipage=many.Count},BackendClient.Json));
        }
        catch(Exception ex) {await File.WriteAllTextAsync(Path.Combine(output,"feature-result.json"),JsonSerializer.Serialize(new {status="failed",checks,error=ex.ToString()},BackendClient.Json)); await SaveNativePreview(Path.Combine(output,"failed.png"));}
        finally {Title="拾简 · 功能验证已结束（隔离数据）";}
    }
}
