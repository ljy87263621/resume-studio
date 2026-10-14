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
            await api.Save("favorites",new Job {Id="compact-neighbor",Title="嵌入式测试工程师",Company="测试公司",Location="广州",Source="BOSS直聘",Url="https://example.com/neighbor"});
            await api.Save("favorites",new Job {Id="compact",Title="嵌入式软件工程师",Company="公司名称\n测试公司",Location="深圳",Source="BOSS直聘",Salary="15–20K",Url="https://example.com/compact",Description=string.Join("\n",Enumerable.Repeat("工作职责与任职资格：开发、调试、测试及文档。",30))});
            foreach(string style in new[]{"classic","haru"}){
                preferences.InterfaceStyle=style;preferences.Theme="light";BuildShell();await Navigate("favorites");await Task.Delay(220);
                // Virtualized containers can be realized after navigation returns, especially
                // on a busy CI desktop. Wait for layout instead of relying on a fixed delay.
                var list=visibleJobList??throw new Exception("Favorite list missing");
                FrameworkElement? first=null,second=null;
                for(int attempt=0;attempt<50;attempt++){
                    list.UpdateLayout();first=list.ContainerFromIndex(0) as FrameworkElement;second=list.ContainerFromIndex(1) as FrameworkElement;
                    if(first is not null&&second is not null&&first.ActualHeight>0&&second.ActualHeight>0)break;
                    await Task.Delay(100);
                }
                if(first is null||second is null)throw new Exception(style+": favorite containers were not realized within five seconds");
                var card=InteractionElement<Expander>(first,e=>e.Tag?.ToString()=="favorite-card")??throw new Exception("Compact favorite card missing");
                double Top(FrameworkElement element)=>element.TransformToVisual(visibleJobList).TransformPoint(new Windows.Foundation.Point()).Y;
                double secondTop=Top(second);
                Check(first.ActualHeight<190&&Math.Abs(secondTop-Top(first)-first.ActualHeight)<=16,style+": collapsed list containers have no reserved blank height");
                double compact=card.ActualHeight;
                Check(!card.IsExpanded&&compact<190,style+": favorite card starts compact without its long description");
                Check(((StackPanel)card.Header).Children.OfType<TextBlock>().Any(t=>t.Text=="测试公司 · 深圳"||t.Text=="测试公司 · 广州"),style+": compact summary folds line breaks and removes captured company labels");
                await SaveNativePreview(Path.Combine(output,"favorites-compact-"+style+".png"));
                card.IsExpanded=true;await Task.Delay(250);
                Check(card.ActualHeight>compact+100&&FeatureButton(body,"编辑收藏") is not null,style+": expanding reveals the full detail and actions");
                Check(Top(second)>secondTop+100&&Math.Abs(Top(second)-Top(first)-first.ActualHeight)<=16,style+": expanding pushes the neighboring card down without overlap");
                card.IsExpanded=false;await Task.Delay(250);Check(card.ActualHeight<190,style+": favorite can be collapsed again");
                Check(Math.Abs(Top(second)-secondTop)<=2,style+": collapsing restores the compact neighbor position");
            }
            string? userDocument=Environment.GetEnvironmentVariable("RESUMESTUDIO_INTERVIEW_DOCUMENT");
            var imports=new[]{"md","txt","docx","blank-headings.docx","numbered.docx","pdf","chinese.pdf"}.ToList();if(!string.IsNullOrWhiteSpace(userDocument))imports.Insert(0,"user-document");
            foreach(string extension in imports){
                string file=extension=="user-document"?userDocument!:Path.Combine(fixtures,extension=="chinese.pdf"?"interview-chinese.pdf":extension=="blank-headings.docx"?"interview-blank-headings.docx":extension=="numbered.docx"?"interview-numbered.docx":"interview."+extension);
                var import=ImportInterviewFile(file);var dialog=await FeatureDialog("导入面试题");
                ListView? list=null;
                for(int attempt=0;attempt<50;attempt++){
                    dialog.UpdateLayout();list=InteractionElement<ListView>(dialog,l=>l.Tag?.ToString()=="interview-import-titles");
                    if(list is not null&&FindTextField(dialog,"题库名称") is not null)break;
                    await Task.Delay(100);
                }
                if(list is null)throw new Exception(extension+": import preview did not finish loading");
                if(extension=="chinese.pdf")(FindTextField(dialog,"题库名称")??throw new Exception("Deck title field missing")).Text="嵌入式基础题";
                int expected=extension=="user-document"?82:2;
                Check(list.Items.Count==expected&&dialog.IsPrimaryButtonEnabled,extension+": native import preview exposes all expected questions");
                await FeatureClick(dialog,"导入题库");await import;await Task.Delay(180);
                Check(currentPage=="interviews"&&InterviewProgress.Count((await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId).Questions).Total==expected,extension+": confirmed import saves a complete deck through Java");
            }
            Check((await api.Get<List<InterviewDeck>>("interviews")).Count==imports.Count,"importing another document preserves previous decks");
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
