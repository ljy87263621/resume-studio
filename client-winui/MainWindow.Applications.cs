using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static readonly string[] ApplicationStages=["已打开","已投递","笔试中","面试中","已录用","已结束"];
    private readonly DispatcherTimer reminderTimer=new() {Interval=TimeSpan.FromMinutes(1)};
    private readonly HashSet<string> seenReminders=[];
    private bool checkingReminders;
    private static bool Pending(ApplicationRecord r)=>!r.ReminderDone&&r.Status is not "已录用" and not "已结束"&&DateTimeOffset.TryParse(r.DueAt,out _);
    private static string ScheduleLabel(ApplicationRecord r)
    {
        if(!DateTimeOffset.TryParse(r.DueAt,out var due)) return "未安排下一次事项";
        string state=r.ReminderDone?"已处理":r.Status is "已录用" or "已结束"?"已结束":due<DateTimeOffset.Now?"已到期":due<=DateTimeOffset.Now.AddHours(24)?"24 小时内":"待办";
        return $"{r.EventKind} · {due.LocalDateTime:yyyy-MM-dd HH:mm} · {state}";
    }
    private async Task CheckApplicationReminders()
    {
        if(checkingReminders||!ready||closing) return; checkingReminders=true;
        try {
            var now=DateTimeOffset.Now; var all=await ReadAgenda();
            var due=ScheduleService.Reminders(all,now).ToList();
            var unseen=due.Where(r=>seenReminders.Add(r.Id+"|"+r.At.ToString("O")+"|"+r.ReminderMinutes+"|"+(r.Overdue(now)?"due":"upcoming"))).ToList();
            if(unseen.Count==0) return;
            notice.Title=$"求职安排提醒 · {due.Count} 项待处理"; notice.Message=string.Join("\n",unseen.Take(3).Select(r=>r.Company+" · "+r.Title+" · "+r.Kind+" · "+r.When))+"\n可在求职日程查看并标记已完成。"; notice.Severity=InfoBarSeverity.Warning; notice.IsOpen=true;
        }
        finally {checkingReminders=false;}
    }
    private async Task ShowApplications()
    {
        Heading("投递记录","跟踪进度、笔试与面试安排、投递快照及复盘。");
        var records=await api.Get<List<ApplicationRecord>>("applications"); if(currentPage!="applications") return;
        var panel=Stack(16); var tools=Row(); tools.Children.Add(Button("＋ 手动添加投递",()=>EditApplication(new ApplicationRecord {Status="已投递"}),true)); tools.Children.Add(Button("刷新安排",()=>Navigate("applications"))); panel.Children.Add(tools);
        panel.Children.Add(Text("安排提前 24 小时及到期时在应用内提醒；关闭应用期间不弹通知，重新打开会检查未处理事项。",12));
        var query=new TextBox {PlaceholderText="搜索公司、职位或备注",Width=280}; var stage=new ComboBox {ItemsSource=new[] {"全部进度"}.Concat(ApplicationStages).ToList(),SelectedIndex=0,Width=140}; var pending=new CheckBox {Content="仅看未处理安排"}; var filters=Row(); filters.Children.Add(query); filters.Children.Add(stage); filters.Children.Add(pending); panel.Children.Add(filters);
        var count=Text("",12); panel.Children.Add(count); var cards=Stack(14); panel.Children.Add(cards);
        void Refresh()
        {
            cards.Children.Clear(); var found=records.Where(r=>(r.Title+" "+r.Company+" "+r.Notes).Contains(query.Text,StringComparison.OrdinalIgnoreCase)&&(stage.SelectedIndex<=0||r.Status==(string)stage.SelectedItem)&&(pending.IsChecked!=true||Pending(r))).OrderBy(r=>Pending(r)?DateTimeOffset.Parse(r.DueAt):DateTimeOffset.MaxValue).ThenByDescending(r=>r.UpdatedAt).ToList();
            count.Text=$"{found.Count} 条记录 · 待处理安排 {records.Count(Pending)} 项";
            foreach(var r in found) {
                var card=Stack(9); card.Children.Add(Text(r.Company+" · "+r.Title,19,true)); card.Children.Add(Text(r.Status+" · "+ScheduleLabel(r),13,true));
                card.Children.Add(Text(r.ResumeSnapshot is null?"简历名称记录："+r.ResumeTitle+"（尚无投递快照）":"投递简历快照："+r.ResumeTitle,12));
                if(!string.IsNullOrWhiteSpace(r.Notes)) card.Children.Add(Text("备注："+r.Notes,12));
                var timeline=Stack(5); foreach(var item in r.Timeline) timeline.Children.Add(Text(LocalDate(item.At)+" · "+item.Status+(item.Note==""?"":" · "+item.Note),12));
                if(r.Timeline.Count==0) timeline.Children.Add(Text("旧记录尚无历史时间线，下次更新进度会记录变化。",12));
                card.Children.Add(new Expander {Header="进度时间线",Content=timeline,HorizontalAlignment=HorizontalAlignment.Stretch});
                if(!string.IsNullOrWhiteSpace(r.Review)) card.Children.Add(new Expander {Header="面试／笔试复盘",Content=Text(r.Review,13),HorizontalAlignment=HorizontalAlignment.Stretch});
                var actions=Row(); actions.Children.Add(Button("更新进度与安排",()=>EditApplication(r),true)); if(r.ResumeSnapshot is not null) actions.Children.Add(Button("查看投递快照",()=>ShowApplicationSnapshot(r)));
                actions.Children.Add(Button("打开原页面 ↗",()=>Launch(r.Url))); actions.Children.Add(Button("删除",async ()=> {if(await Confirm("删除投递记录？",r.Company+" · "+r.Title)) {await api.Delete("applications/"+r.Id); await Navigate("applications");}})); card.Children.Add(actions); cards.Children.Add(Card(card));
            }
            if(found.Count==0) cards.Children.Add(Text("暂无符合条件的投递记录。",15));
        }
        query.TextChanged+=(_,_)=>Refresh(); stage.SelectionChanged+=(_,_)=>Refresh(); pending.Checked+=(_,_)=>Refresh(); pending.Unchecked+=(_,_)=>Refresh(); Refresh(); FillScroll(panel);
    }
    private async Task EditApplication(ApplicationRecord original,string returnPage="applications")
    {
        var r=JsonSerializer.Deserialize<ApplicationRecord>(JsonSerializer.Serialize(original,BackendClient.Json),BackendClient.Json)!;
        var resumes=await api.Get<List<Resume>>("resumes"); resumes.Insert(0,new Resume {Id="",Title="未绑定简历"});
        var panel=Stack(12); var title=new TextBox {Header="职位名称",Text=r.Title,MaxLength=250}; var company=new TextBox {Header="公司",Text=r.Company,MaxLength=200}; var url=new TextBox {Header="招聘页面链接（HTTPS）",Text=r.Url,MaxLength=3000};
        var status=new ComboBox {Header="当前进度",ItemsSource=ApplicationStages,SelectedItem=r.Status,HorizontalAlignment=HorizontalAlignment.Stretch};
        var resume=new ComboBox {Header="确认已使用的简历（保存后固定为快照）",ItemsSource=resumes,DisplayMemberPath="Title",SelectedIndex=0,IsEnabled=r.ResumeSnapshot is null,HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var element in new UIElement[] {title,company,url,status,resume}) panel.Children.Add(element);
        if(r.ResumeSnapshot is not null) panel.Children.Add(Text("已固定投递快照："+r.ResumeTitle+"。之后修改简历不会改变此快照。",12));
        var scheduled=new CheckBox {Content="有下一次笔试／面试／跟进安排",IsChecked=!string.IsNullOrEmpty(r.DueAt)}; panel.Children.Add(scheduled);
        var kind=new ComboBox {Header="安排类型",ItemsSource=new[] {"笔试","面试","跟进","截止日期"},SelectedItem=r.EventKind,HorizontalAlignment=HorizontalAlignment.Stretch};
        DateTimeOffset due=DateTimeOffset.TryParse(r.DueAt,out var parsed)?parsed.ToLocalTime():DateTimeOffset.Now.AddDays(1);
        var date=new CalendarDatePicker {Header="日期",Date=due,HorizontalAlignment=HorizontalAlignment.Stretch}; var time=new TimePicker {Header="时间",ClockIdentifier="24HourClock",Time=due.TimeOfDay,HorizontalAlignment=HorizontalAlignment.Stretch};
        var done=new CheckBox {Content="这次安排已处理，不再提醒",IsChecked=r.ReminderDone}; panel.Children.Add(kind); panel.Children.Add(date); panel.Children.Add(time); panel.Children.Add(done);
        var notes=new TextBox {Header="备注／地点／会议链接",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=r.Notes,MinHeight=85,MaxLength=10000};
        var review=new TextBox {Header="笔试／面试复盘",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=r.Review,PlaceholderText="题目、回答、反馈、待补知识和下次改进",MinHeight=140,MaxLength=10000}; panel.Children.Add(notes); panel.Children.Add(review);
        void ScheduleEnabled() {kind.IsEnabled=date.IsEnabled=time.IsEnabled=done.IsEnabled=scheduled.IsChecked==true;}
        scheduled.Checked+=(_,_)=>ScheduleEnabled(); scheduled.Unchecked+=(_,_)=>ScheduleEnabled(); ScheduleEnabled();
        if(await new ContentDialog {XamlRoot=shell.XamlRoot,Title="更新投递记录",Content=new ScrollViewer {Content=panel,MaxHeight=590},PrimaryButtonText="保存记录",CloseButtonText="返回"}.ShowAsync()!=ContentDialogResult.Primary) return;
        r.Title=title.Text.Trim(); r.Company=company.Text.Trim(); r.Url=url.Text.Trim(); r.Status=(string)status.SelectedItem; r.Notes=notes.Text; r.Review=review.Text; r.EventKind=kind.SelectedItem as string??"面试";
        r.DueAt=""; if(scheduled.IsChecked==true) {if(date.Date is null) throw new InvalidOperationException("请选择安排日期。"); var local=date.Date.Value.Date+time.Time; r.DueAt=new DateTimeOffset(local,TimeZoneInfo.Local.GetUtcOffset(local)).ToString("O");}
        r.ReminderDone=done.IsChecked==true; if(r.DueAt!=original.DueAt) r.ReminderDone=false;
        if(r.ResumeSnapshot is null&&resume.SelectedItem is Resume selected&&!string.IsNullOrEmpty(selected.Id)) {
            if(r.Status=="已打开") throw new InvalidOperationException("仅打开页面尚未投递，请确认实际使用简历后更新为已投递或后续进度。");
            r.ResumeSnapshot=CloneResume(selected); r.ResumeTitle=selected.Title;
        }
        await api.Save("applications",r); await Navigate(returnPage); await CheckApplicationReminders();
    }
    private async Task ShowApplicationSnapshot(ApplicationRecord r)
    {
        if(r.ResumeSnapshot is null) return; var snapshot=r.ResumeSnapshot; var panel=Stack(12); panel.Children.Add(Text(snapshot.Title,19,true));
        panel.Children.Add(Text(snapshot.Profile.Name+" · "+snapshot.Profile.Role,14));
        foreach(var section in snapshot.Sections.Where(s=>s.Visible)) {panel.Children.Add(Text(section.Title,16,true)); panel.Children.Add(Text(section.Content,13));}
        if(await new ContentDialog {XamlRoot=shell.XamlRoot,Title="投递时的简历快照",Content=new ScrollViewer {Content=panel,MaxHeight=590},PrimaryButtonText="复制为新简历",CloseButtonText="返回"}.ShowAsync()!=ContentDialogResult.Primary) return;
        await SaveCurrent(); var copy=CloneResume(snapshot); copy.Id=Guid.NewGuid().ToString(); copy.Title+=" · 投递快照副本"; current=await api.Save("resumes",copy); dirty=false; await OpenEditor();
    }
}
