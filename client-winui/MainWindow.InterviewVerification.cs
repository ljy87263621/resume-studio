using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task InterviewFeatureTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"interview-test");
        string fixtures=Environment.GetEnvironmentVariable("RESUMESTUDIO_INTERVIEW_FIXTURES")??throw new IOException("Import fixtures missing");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        async Task Click(string label){var button=FeatureButton(body,label)??throw new Exception("Action missing: "+label);((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(180);}
        try {
            await api.Save("favorites",new Job {Id="compact",Title="嵌入式软件工程师",Company="公司名称\n测试公司",Location="深圳",Source="BOSS直聘",Salary="15–20K",Url="https://example.com/compact",Description=string.Join("\n",Enumerable.Repeat("工作职责与任职资格：开发、调试、测试及文档。",30))});
            foreach(string style in new[]{"classic","haru"}){
                preferences.InterfaceStyle=style;preferences.Theme="light";BuildShell();await Navigate("favorites");await Task.Delay(220);
                var card=InteractionElement<Expander>(body,e=>e.Tag?.ToString()=="favorite-card")??throw new Exception("Compact favorite card missing");
                double compact=card.ActualHeight;
                Check(!card.IsExpanded&&compact<190,style+": favorite card starts compact without its long description");
                Check(((StackPanel)card.Header).Children.OfType<TextBlock>().Any(t=>t.Text=="测试公司 · 深圳"),style+": compact summary folds line breaks and removes captured company labels");
                await SaveNativePreview(Path.Combine(output,"favorites-compact-"+style+".png"));
                card.IsExpanded=true;await Task.Delay(250);
                Check(card.ActualHeight>compact+100&&FeatureButton(body,"编辑收藏") is not null,style+": expanding reveals the full detail and actions");
                card.IsExpanded=false;await Task.Delay(250);Check(card.ActualHeight<190,style+": favorite can be collapsed again");
            }
            foreach(string extension in new[]{"md","txt","docx","pdf","chinese.pdf"}){
                var import=ImportInterviewFile(Path.Combine(fixtures,extension=="chinese.pdf"?"interview-chinese.pdf":"interview."+extension));var dialog=await FeatureDialog("导入面试题");
                if(extension=="chinese.pdf")FindTextField(dialog,"题库名称")!.Text="嵌入式基础题";
                var list=InteractionElement<ListView>(dialog,l=>l.Tag?.ToString()=="interview-import-titles")!;
                Check(list.Items.Count==2&&dialog.IsPrimaryButtonEnabled,extension+": native import preview exposes two questions");
                await FeatureClick(dialog,"导入题库");await import;await Task.Delay(180);
                Check(currentPage=="interviews"&&InterviewProgress.Count((await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId).Questions).Total==2,extension+": confirmed import saves a complete deck through Java");
            }
            Check((await api.Get<List<InterviewDeck>>("interviews")).Count==5,"importing another document preserves previous decks");
            await Click("✓ 已记住");var deck=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId);
            Check(deck.Questions[0].Status=="remembered"&&interviewIndex==1,"remembered verdict persists and advances to the second card");
            await Click("未掌握");deck=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId);
            Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,1,1,0),"both buttons produce separate persisted statistics");
            await Click("← 上一题");Check(interviewIndex==0,"mouse previous action returns to the first card");
            await Click("未掌握");deck=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId);
            Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,0,2,0),"changing a remembered card to unmastered replaces rather than duplicates its count");
            var filter=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="interview-filter")!;filter.SelectedIndex=2;await Task.Delay(100);
            Check(InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="interview-position")!.Text=="0 / 0","empty remembered filter has no stale card");
            filter.SelectedIndex=1;await Task.Delay(100);
            Check(FeatureButton(body,"✓ 已记住") is not null,"unmastered practice filter shows the relevant question");
            await SaveNativePreview(Path.Combine(output,"interview-study.png"));
            string savedId=interviewDeckId;await Navigate("templates");await Navigate("interviews");await Task.Delay(120);
            Check(interviewDeckId==savedId&&InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="interview-stats")!.Text.Contains("未掌握 2"),"returning to study restores saved verdicts and statistics");
            var answer=InteractionElement<ToggleSwitch>(body,t=>t.Header?.ToString()=="显示答案")!;answer.IsOn=false;await Task.Delay(100);answer.IsOn=true;
            Check(body.KeyboardAccelerators.Count==2,"question page exposes both direction shortcuts");
            await File.WriteAllTextAsync(Path.Combine(output,"interview-result.json"),JsonSerializer.Serialize(new{status="passed",checks},BackendClient.Json));
        }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"interview-result.json"),JsonSerializer.Serialize(new{status="failed",checks,error=ex.ToString()},BackendClient.Json));}
        finally {await SaveCurrent();await DetachPreviewAsync(true);await api.StopAsync();closing=true;api.Dispose();Close();}
    }
}
