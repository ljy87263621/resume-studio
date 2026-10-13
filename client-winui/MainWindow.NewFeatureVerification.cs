using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task VerifyNewFeatures(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        await Navigate("templates");await Task.Delay(250);
        Check(FeatureButton(body,"预览带照片版") is not null&&FeatureButton(body,"预览无照片版") is not null,"native template cards expose both side-by-side photo variants");
        Check(templates.All(t=>!t.SupportsPhoto||File.Exists(Path.Combine(AppContext.BaseDirectory,"Assets","Templates",t.Id+"-photo.png"))),"every photo template has a bundled photo-placeholder thumbnail");
        await SaveNativePreview(Path.Combine(output,"templates-paired.png"));
        var template=templates.Single(t=>t.Id=="gh-back-awesome");
        var task=PreviewTemplate(template,true);var dialog=await FeatureDialog(template.Name+" · 证件照占位预览");var web=(WebView2)dialog.Content;
        bool loaded=false;for(int n=0;n<160;n++){if(web.CoreWebView2 is not null&&await web.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.photo-placeholder')&&Array.from(document.fonts).some(f=>f.family==='MiSans'&&f.status==='loaded')")=="true"){loaded=true;break;}await Task.Delay(50);}Check(loaded,"photo preview renders a placeholder using the bundled MiSans font");
        await FeatureClick(dialog,"使用模板");await task;Check(current!.Photo==""&&current.FontFamily=="MiSans","using photo preview creates a clean resume with MiSans default, without saving a fake photo");
        ToggleLayoutSettings();await Task.Delay(160);var layoutContent=editorLayoutPanel!;var font=FindCombo(layoutContent,"简历字体")??throw new Exception("Font selector missing");font.SelectedIndex=2;await Task.Delay(160);await SaveCurrent();await WaitForPreview();
        Check((await api.Get<List<Resume>>("resumes")).Single(r=>r.Id==current.Id).FontFamily=="SimSun"&& (await preview!.CoreWebView2!.ExecuteScriptAsync("getComputedStyle(document.querySelector('article')).fontFamily")).Contains("SimSun"),"native font choice is saved and applied to actual preview");
        font.SelectedIndex=0;ToggleLayoutSettings();await Task.Delay(160);await SaveCurrent();await WaitForPreview();string pdf=Path.Combine(output,"misans-resume.pdf");await PrintPdf(pdf);
        Check(Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf)).Contains("MiSans"),"exported PDF embeds MiSans rather than relying on installed system fonts");
        var original=await api.Save("applications",new ApplicationRecord {Title="日程联动笔试",Company="测试公司",Url="https://example.com/schedule-test",Status="笔试中",DueAt=DateTimeOffset.Now.AddHours(2).ToString("O"),EventKind="笔试"});
        await Navigate("schedule");var edit=EditSchedule(new ScheduleItem {StartAt=DateTimeOffset.Now.AddHours(3).ToString("O")});var scheduleDialog=await FeatureDialog("编辑日程");
        (FindTextField(scheduleDialog,"事项名称")??throw new Exception("Schedule title missing")).Text="自定义面试安排";var kind=FindCombo(scheduleDialog,"日程类型")!;kind.SelectedItem="面试";await FeatureClick(scheduleDialog,"保存日程");await edit;
        var items=await api.Get<List<ScheduleItem>>("schedule");var saved=items.Single(i=>i.Title=="自定义面试安排");Check(saved.Kind=="面试"&&DateTimeOffset.TryParse(saved.StartAt,out _),"native schedule dialog saves an event through the backend");
        var editing=EditSchedule(saved);var existing=await FeatureDialog("编辑日程");var fields=(StackPanel)((ScrollViewer)existing.Content).Content;
        var dayPicker=fields.Children.OfType<CalendarDatePicker>().Single();dayPicker.Date=DateTimeOffset.Now.AddDays(1);
        fields.Children.OfType<CheckBox>().Single(b=>b.Content?.ToString()=="全天事项").IsChecked=true;
        fields.Children.OfType<CheckBox>().Single(b=>b.Content?.ToString()=="软件内提醒").IsChecked=false;
        Check(!fields.Children.OfType<TimePicker>().Single().IsEnabled,"all-day toggle disables the native time picker");
        await FeatureClick(existing,"保存日程");await editing;saved=(await api.Get<List<ScheduleItem>>("schedule")).Single(i=>i.Id==saved.Id);
        Check(saved.AllDay&&!saved.ReminderEnabled&&DateTimeOffset.Parse(saved.StartAt).LocalDateTime.Date==DateTime.Today.AddDays(1),"editing a schedule moves its date and persists all-day and reminder settings");
        var agenda=await ReadAgenda();Check(agenda.Any(e=>e.Application?.Id==original.Id)&&agenda.Any(e=>e.Item?.Id==saved.Id),"calendar merges independent events with existing application arrangements");
        Check(scheduleCalendar is not null&&scheduleCalendar.SelectedDates.Count==1,"native monthly calendar selects a date");
        scheduleCalendar!.SelectedDates.Clear();scheduleCalendar.SelectedDates.Add(new DateTimeOffset(DateTime.Today.AddDays(2)));Check(scheduleDate==DateTime.Today.AddDays(2)&&scheduleView==0,"calendar date selection updates the selected-day list");
        scheduleView=1;await Navigate("schedule");await SaveNativePreview(Path.Combine(output,"schedule-classic.png"));
        var projected=(await ReadAgenda()).Single(e=>e.Application?.Id==original.Id);await SetAgendaCompleted(projected,true);Check((await api.Get<List<ApplicationRecord>>("applications")).Single(r=>r.Id==original.Id).ReminderDone,"calendar completion updates the original application reminder state");
        var upcoming=(await ReadAgenda()).Single(e=>e.Item?.Id==saved.Id);await SetAgendaCompleted(upcoming,true);Check((await api.Get<List<ScheduleItem>>("schedule")).Single(i=>i.Id==saved.Id).Completed,"calendar completion persists for an independent event");
        saved=await api.Save("schedule",new ScheduleItem {Title="日程提醒验证",StartAt=DateTimeOffset.Now.AddMinutes(-2).ToString("O"),Kind="投递截止",ReminderMinutes=0});seenReminders.Clear();await CheckApplicationReminders();Check(notice.Message.Contains("日程提醒验证"),"custom schedule reminder appears in the running app");
        await api.Save("schedule",new ScheduleItem {Title="提交岗位简历",StartAt=DateTimeOffset.Now.AddHours(5).ToString("O"),Kind="投递截止",ReminderMinutes=60});scheduleView=2;
        preferences.InterfaceStyle="haru";await ApplyPreferences();await Navigate("schedule");await SaveNativePreview(Path.Combine(output,"schedule-haru.png"));Check(modernNavigation is not null&&pageTitle.FontFamily.Source.Contains("MiSans"),"Haru calendar and interface text use the bundled MiSans family");
        await Navigate("templates");await SaveNativePreview(Path.Combine(output,"templates-paired-haru.png"));
        await api.Delete("schedule/"+saved.Id);Check(!(await api.Get<List<ScheduleItem>>("schedule")).Any(i=>i.Id==saved.Id),"schedule deletion removes only the selected independent event");
        preferences.InterfaceStyle="classic";await ApplyPreferences();
    }
}
