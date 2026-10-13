using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private DateTime scheduleDate=DateTime.Today;
    private int scheduleView;
    private CalendarView? scheduleCalendar;
    private async Task<List<AgendaEntry>> ReadAgenda()
    {
        var items=api.Get<List<ScheduleItem>>("schedule"); var records=api.Get<List<ApplicationRecord>>("applications");
        await Task.WhenAll(items,records); return ScheduleService.Merge(items.Result,records.Result);
    }
    private async Task ShowSchedule()
    {
        Heading("求职日程","把笔试、面试、投递截止和自己的待办安排放在一起。");
        var entries=await ReadAgenda(); if(currentPage!="schedule") return;
        var panel=Stack(16); var tools=Row();
        tools.Children.Add(Button("＋ 新增日程",()=>EditSchedule(new ScheduleItem {StartAt=new DateTimeOffset(scheduleDate.AddHours(9),TimeZoneInfo.Local.GetUtcOffset(scheduleDate)).ToString("O")}),true));
        tools.Children.Add(Button("今天",async ()=> {scheduleDate=DateTime.Today; scheduleView=0; await Navigate("schedule");})); tools.Children.Add(Button("刷新",()=>Navigate("schedule"))); panel.Children.Add(tools);
        panel.Children.Add(Text("投递记录中的下一次安排会自动显示；在日程中可另外添加多次面试或自定义事项。提醒只在软件运行时显示，重新打开会检查未处理事项。",12));
        var grid=new Grid {ColumnSpacing=18,RowSpacing=18}; grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto}); grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)}); grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        scheduleCalendar=new CalendarView {SelectionMode=CalendarViewSelectionMode.Single,Language="zh-CN",FirstDayOfWeek=Windows.Globalization.DayOfWeek.Monday,HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=320,MinDate=new DateTimeOffset(new DateTime(2000,1,1)),MaxDate=new DateTimeOffset(new DateTime(2100,12,31))};
        var calendar=scheduleCalendar;
        calendar.SetDisplayDate(new DateTimeOffset(scheduleDate)); calendar.SelectedDates.Add(new DateTimeOffset(scheduleDate));
        scheduleCalendar.CalendarViewDayItemChanging+=(_,args)=> {var date=args.Item.Date.Date; args.Item.SetDensityColors(ScheduleService.ForDate(entries,date).Where(e=>!e.Completed).Take(10).Select(e=>((SolidColorBrush)Brush(e.Kind=="面试"?"#245AE8":e.Kind=="笔试"?"#167D78":e.Kind=="投递截止"?"#CC7B20":"#7955B4")).Color));};
        var month=Stack(10); month.Children.Add(Text("月历",18,true)); month.Children.Add(scheduleCalendar); month.Children.Add(Text("日期上的色条表示待办：蓝色面试 · 绿色笔试 · 橙色截止 · 紫色其他",11));
        var monthCard=Card(month,new Thickness(16)); monthCard.MaxWidth=430; monthCard.VerticalAlignment=VerticalAlignment.Top; grid.Children.Add(monthCard);
        var list=Stack(12); var heading=Text("",18,true); list.Children.Add(heading);
        var mode=new ComboBox {Header="查看范围",ItemsSource=new[] {"所选日期","近期 30 天","全部未完成"},SelectedIndex=scheduleView,HorizontalAlignment=HorizontalAlignment.Stretch}; list.Children.Add(mode);
        var query=new TextBox {PlaceholderText="搜索事项、公司或备注",MaxLength=200}; list.Children.Add(query); var cards=Stack(12); list.Children.Add(cards);
        void Refresh()
        {
            cards.Children.Clear(); scheduleView=mode.SelectedIndex; var now=DateTimeOffset.Now;
            heading.Text=scheduleView==0?$"{scheduleDate:yyyy年M月d日} · 日程":scheduleView==1?"近期日程":"全部待办";
            var found=(scheduleView==0?ScheduleService.ForDate(entries,scheduleDate):scheduleView==1?ScheduleService.Upcoming(entries,now):entries.Where(e=>!e.Completed)).Where(e=>(e.Title+" "+e.Company+" "+e.Item?.Notes+" "+e.Application?.Notes).Contains(query.Text,StringComparison.OrdinalIgnoreCase)).ToList();
            if(found.Count==0) cards.Children.Add(Text(scheduleView==0?"这一天暂无安排，可以新增日程。":"暂无符合条件的日程。",14));
            foreach(var entry in found) {
                var item=Stack(8); item.Children.Add(Text(entry.Title,17,true)); item.Children.Add(Text(entry.Kind+" · "+entry.When+" · "+(entry.Completed?"已完成":entry.Overdue(now)?"已到期":"待办"),12,true));
                if(entry.Company.Length>0) item.Children.Add(Text(entry.Company,12));
                string notes=entry.Item?.Notes??entry.Application?.Notes??""; if(notes.Length>0) item.Children.Add(Text(notes,12));
                item.Children.Add(Text(entry.Application is not null?"来自投递记录 · 修改与完成状态同步":"独立日程"+(entry.Item?.ApplicationId.Length>0?" · 已关联投递":"")+(entry.ReminderEnabled?" · "+ScheduleService.ReminderLabel(entry.ReminderMinutes):" · 提醒已关闭"),11));
                var actions=Row(7);
                actions.Children.Add(Button("编辑",()=>entry.Item is not null?EditSchedule(entry.Item):EditApplication(entry.Application!,"schedule")));
                var complete=Button(entry.Completed?"恢复待办":"标记完成",async ()=> {await SetAgendaCompleted(entry,!entry.Completed); await Navigate("schedule");});
                complete.IsEnabled=entry.Item is not null||entry.Application?.Status is not "已录用" and not "已结束";actions.Children.Add(complete);
                if(entry.Item is not null) actions.Children.Add(Button("删除",async ()=> {if(await Confirm("删除这条日程？",entry.Title)){await api.Delete("schedule/"+entry.Item.Id);await Navigate("schedule");}}));
                if(entry.Item is not null&&entry.Item.ApplicationId.Length>0) actions.Children.Add(Button("查看投递",()=>Navigate("applications")));
                item.Children.Add(actions); cards.Children.Add(Card(item,new Thickness(16)));
            }
        }
        calendar.SelectedDatesChanged+=(_,_)=> {if(calendar.SelectedDates.Count>0){scheduleDate=calendar.SelectedDates[0].Date;mode.SelectedIndex=0;Refresh();}};
        mode.SelectionChanged+=(_,_)=>Refresh(); query.TextChanged+=(_,_)=>Refresh(); Refresh();
        var listCard=Card(list,new Thickness(18)); Grid.SetColumn(listCard,1); grid.Children.Add(listCard);
        bool stacked=false; grid.SizeChanged+=(_,args)=> {bool next=args.NewSize.Width<780;if(next==stacked)return;stacked=next;grid.ColumnDefinitions[0].Width=next?new GridLength(1,GridUnitType.Star):GridLength.Auto;grid.ColumnDefinitions[1].Width=next?new GridLength(0):new GridLength(1,GridUnitType.Star);Grid.SetColumn(listCard,next?0:1);Grid.SetRow(listCard,next?1:0);};
        panel.Children.Add(grid); FillScroll(panel);
    }
    private async Task SetAgendaCompleted(AgendaEntry entry,bool completed)
    {
        if(entry.Item is not null){entry.Item.Completed=completed;await api.Save("schedule",entry.Item);}
        else if(entry.Application is not null){entry.Application.ReminderDone=completed;await api.Save("applications",entry.Application);}
    }
    private async Task EditSchedule(ScheduleItem original)
    {
        var item=JsonSerializer.Deserialize<ScheduleItem>(JsonSerializer.Serialize(original,BackendClient.Json),BackendClient.Json)!;
        var applications=await api.Get<List<ApplicationRecord>>("applications");
        var links=new[] {new ApplicationRecord {Id="",Title="不关联投递"}}.Concat(applications).ToList();
        var panel=Stack(12); var title=new TextBox {Header="事项名称",Text=item.Title,MaxLength=200};
        var kind=new ComboBox {Header="日程类型",ItemsSource=ScheduleService.Kinds,SelectedItem=item.Kind,HorizontalAlignment=HorizontalAlignment.Stretch};
        var at=DateTimeOffset.TryParse(item.StartAt,out var parsed)?parsed.ToLocalTime():DateTimeOffset.Now;
        var date=new CalendarDatePicker {Header="日期",Date=at,HorizontalAlignment=HorizontalAlignment.Stretch};var time=new TimePicker {Header="时间",ClockIdentifier="24HourClock",Time=at.TimeOfDay,HorizontalAlignment=HorizontalAlignment.Stretch};
        var allDay=new CheckBox {Content="全天事项",IsChecked=item.AllDay}; time.IsEnabled=!item.AllDay; allDay.Checked+=(_,_)=>time.IsEnabled=false;allDay.Unchecked+=(_,_)=>time.IsEnabled=true;
        var link=new ComboBox {Header="关联投递（可选）",ItemsSource=links,DisplayMemberPath="Title",SelectedItem=links.FirstOrDefault(r=>r.Id==item.ApplicationId)??links[0],HorizontalAlignment=HorizontalAlignment.Stretch};
        var reminders=new CheckBox {Content="软件内提醒",IsChecked=item.ReminderEnabled};int[] minutes=[1440,60,15,0];var ahead=new ComboBox {Header="提醒时间",ItemsSource=new[] {"提前一天","提前一小时","提前 15 分钟","到点提醒"},SelectedIndex=Math.Max(0,Array.IndexOf(minutes,item.ReminderMinutes)),HorizontalAlignment=HorizontalAlignment.Stretch,IsEnabled=item.ReminderEnabled};reminders.Checked+=(_,_)=>ahead.IsEnabled=true;reminders.Unchecked+=(_,_)=>ahead.IsEnabled=false;
        var notes=new TextBox {Header="备注／地点／会议链接",Text=item.Notes,MaxLength=10000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=90};var completed=new CheckBox {Content="已完成",IsChecked=item.Completed};
        foreach(var element in new UIElement[] {title,kind,date,allDay,time,link,reminders,ahead,notes,completed}) panel.Children.Add(element);
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title="编辑日程",Content=new ScrollViewer {Content=panel,MaxHeight=580},PrimaryButtonText="保存日程",CloseButtonText="返回"};
        dialog.PrimaryButtonClick+=(_,args)=> {if(string.IsNullOrWhiteSpace(title.Text)||date.Date is null){args.Cancel=true;title.Description="请填写事项名称并选择日期。";}};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var local=date.Date!.Value.Date+(allDay.IsChecked==true?TimeSpan.Zero:time.Time);
        item.Title=title.Text.Trim();item.Kind=(string)kind.SelectedItem;item.StartAt=new DateTimeOffset(local,TimeZoneInfo.Local.GetUtcOffset(local)).ToString("O");item.AllDay=allDay.IsChecked==true;item.Completed=completed.IsChecked==true;item.ReminderEnabled=reminders.IsChecked==true;item.ReminderMinutes=minutes[ahead.SelectedIndex];item.Notes=notes.Text;item.ApplicationId=(link.SelectedItem as ApplicationRecord)?.Id??"";
        await api.Save("schedule",item);scheduleDate=local.Date;await Navigate("schedule");await CheckApplicationReminders();
    }
}
