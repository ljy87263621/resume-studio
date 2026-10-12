using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;
using Windows.UI.ViewManagement;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private AppPreferences preferences = new();
    private static bool haruStyle, darkStyle;
    private Grid? contentHost;
    private Border? headerFrame;
    private NavigationView? modernNavigation;
    private readonly UISettings systemSettings = new();
    private bool changingNavigation;
    private static string Accent => haruStyle ? "#5B6EAE" : "#245AE8";
    private static string Ink => darkStyle ? "#ECECF5" : haruStyle ? "#292D3E" : "#22304A";
    private static string Surface => darkStyle ? "#24242D" : "#FFFFFF";
    private static string Stroke => darkStyle ? "#41414D" : haruStyle ? "#E3E3EC" : "#E3E8F1";
    private static readonly (string Key,string Label,string Glyph)[] Pages = [
        ("templates","模板中心","\uE8A5"),("resumes","我的简历","\uE8A5"),("jobs","发现职位","\uE721"),
        ("sources","公司招聘官网","\uE774"),("favorites","职位收藏","\uE734"),("applications","投递记录","\uE73E"),
        ("about","使用说明","\uE946"),("settings","设置","\uE713")
    ];
    private void BuildContentHost()
    {
        contentHost=new Grid();
        contentHost.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        contentHost.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        contentHost.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        var header=new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var heading=Stack(5); heading.Children.Add(pageTitle); heading.Children.Add(pageSubtitle); header.Children.Add(heading);
        saveState.HorizontalAlignment=HorizontalAlignment.Right; saveState.VerticalAlignment=VerticalAlignment.Center; saveState.Margin=new Thickness(16,0,0,0); Grid.SetColumn(saveState,1); header.Children.Add(saveState);
        headerFrame=new Border { Child=header,Margin=new Thickness(0,0,0,20) }; contentHost.Children.Add(headerFrame);
        Grid.SetRow(notice,1); contentHost.Children.Add(notice); Grid.SetRow(body,2); contentHost.Children.Add(body);
    }
    private void BuildShell()
    {
        haruStyle=preferences.InterfaceStyle=="haru";
        var systemColor=systemSettings.GetColorValue(UIColorType.Background);
        darkStyle=haruStyle&&(preferences.Theme=="dark"||preferences.Theme=="system"&&systemColor.R+systemColor.G+systemColor.B<384);
        shell.RequestedTheme=darkStyle?ElementTheme.Dark:ElementTheme.Light;
        if(modernNavigation is not null) { modernNavigation.Content=null; modernNavigation=null; }
        shell.Children.Clear(); shell.ColumnDefinitions.Clear(); navigation.Clear();
        contentHost!.Resources.Clear();
        if(haruStyle)
        {
            foreach(var type in new[] {typeof(TextBox),typeof(ComboBox)})
            {
                var style=new Style(type); style.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(12))); style.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("MiSans"))); contentHost.Resources.Add(type,style);
            }
        }
        shell.Background=Brush(darkStyle?"#1B1B22":haruStyle?"#F7F7F9":"#F5F7FB");
        contentHost!.Margin=haruStyle?new Thickness(22,18,22,18):new Thickness(30,26,30,22);
        pageTitle.Foreground=Brush(Ink); pageSubtitle.Foreground=Brush(darkStyle?"#B1B1C3":"#697086"); saveState.Foreground=Brush(darkStyle?"#9FB2E5":"#167D78");
        headerFrame!.Padding=haruStyle?new Thickness(20,15,20,15):new Thickness(0);
        headerFrame.CornerRadius=new CornerRadius(haruStyle?22:0);
        headerFrame.BorderThickness=new Thickness(haruStyle?1:0); headerFrame.BorderBrush=Brush(Stroke);
        headerFrame.Background=haruStyle?new LinearGradientBrush {
            StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(0,1),
            GradientStops={new GradientStop { Color=((SolidColorBrush)Brush(darkStyle?"#E62B2B36":"#EFFFFFFF")).Color,Offset=0 },new GradientStop { Color=((SolidColorBrush)Brush(darkStyle?"#CC24242D":"#CCF4F4F8")).Color,Offset=1 }}
        }:new SolidColorBrush(Colors.Transparent);
        previewTimer.Interval=TimeSpan.FromMilliseconds(preferences.PreviewDelayMs);
        if(haruStyle)
        {
            Grid.SetColumn(contentHost,0);
            modernNavigation=new NavigationView { IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsSettingsVisible=false,OpenPaneLength=210,CompactPaneLength=48,PaneDisplayMode=NavigationViewPaneDisplayMode.Auto,Content=contentHost };
            modernNavigation.PaneTitle="拾简";
            foreach(var page in Pages)
            {
                var item=new NavigationViewItem { Content=page.Label,Tag=page.Key,Icon=new FontIcon { Glyph=page.Glyph,FontSize=16 } };
                if(page.Key is "settings" or "about") modernNavigation.FooterMenuItems.Add(item); else modernNavigation.MenuItems.Add(item);
            }
            modernNavigation.SelectionChanged+=async (_,e)=> { if(!changingNavigation&&e.SelectedItem is NavigationViewItem item) await Run(()=>Navigate((string)item.Tag)); };
            shell.Children.Add(modernNavigation);
        }
        else
        {
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(222) }); shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            BuildSidebar(); Grid.SetColumn(contentHost,1); shell.Children.Add(contentHost);
        }
        UpdateNavigation(currentPage);
    }
    private void UpdateNavigation(string page)
    {
        string selected=page=="editor"?"resumes":page;
        foreach(var button in navigation) button.Background=Brush((string)button.Tag==selected?"#294267":"#14233F");
        if(modernNavigation is null) return;
        changingNavigation=true;
        try { modernNavigation.SelectedItem=modernNavigation.MenuItems.Concat(modernNavigation.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(x=>(string)x.Tag==selected); }
        finally { changingNavigation=false; }
    }
    private async Task ApplyPreferences()
    {
        await SaveCurrent(); await preferences.SaveAsync(api.DataDirectory); DetachPreview(); BuildShell();
        if(currentPage=="editor") await OpenEditor(); else await Navigate(currentPage);
    }
    private void ShowSettings()
    {
        Heading("设置","切换界面外观与预览节奏，简历和求职记录共用同一份本机数据。");
        var panel=Stack(18);
        var appearance=Stack(14); appearance.Children.Add(Text("界面与主题",20,true));
        var style=new ComboBox { Header="界面版本",ItemsSource=new[] {"经典界面 · 当前默认","Haru 界面 · haruphoto 设计语言"},SelectedIndex=haruStyle?1:0,HorizontalAlignment=HorizontalAlignment.Stretch };
        style.SelectionChanged+=async (_,_)=>await Run(async ()=> { preferences.InterfaceStyle=style.SelectedIndex==1?"haru":"classic"; await ApplyPreferences(); }); appearance.Children.Add(style);
        var theme=new ComboBox { Header="Haru 界面主题",ItemsSource=new[] {"跟随系统","浅色","深色"},SelectedIndex=preferences.Theme=="light"?1:preferences.Theme=="dark"?2:0,IsEnabled=haruStyle,HorizontalAlignment=HorizontalAlignment.Stretch };
        theme.SelectionChanged+=async (_,_)=>await Run(async ()=> { preferences.Theme=theme.SelectedIndex==1?"light":theme.SelectedIndex==2?"dark":"system"; await ApplyPreferences(); }); appearance.Children.Add(theme);
        appearance.Children.Add(Text("Haru 使用柔和蓝紫色、原生侧边导航、圆角卡片和轻玻璃顶栏。经典界面保持原有外观。切换无需重启。",13)); panel.Children.Add(Card(appearance));
        var performance=Stack(14); performance.Children.Add(Text("编辑与性能",20,true));
        var live=new ToggleSwitch { Header="实时预览",IsOn=preferences.LivePreview };
        live.Toggled+=async (_,_)=>await Run(async ()=> { preferences.LivePreview=live.IsOn; if(!live.IsOn) previewTimer.Stop(); await preferences.SaveAsync(api.DataDirectory); }); performance.Children.Add(live);
        var delay=new ComboBox { Header="停止输入后的预览刷新",ItemsSource=new[] {"及时 · 350 ms","均衡 · 900 ms","省电 · 1500 ms"},SelectedIndex=preferences.PreviewDelayMs<650?0:preferences.PreviewDelayMs<1200?1:2,HorizontalAlignment=HorizontalAlignment.Stretch };
        delay.SelectionChanged+=async (_,_)=>await Run(async ()=> { preferences.PreviewDelayMs=delay.SelectedIndex==0?350:delay.SelectedIndex==2?1500:900; previewTimer.Interval=TimeSpan.FromMilliseconds(preferences.PreviewDelayMs); await preferences.SaveAsync(api.DataDirectory); }); performance.Children.Add(delay);
        performance.Children.Add(Text("自动保存独立运行，停止输入后约 650 ms 保存。关闭实时预览后可在工作台手动刷新，导出 PDF 时仍会同步最新内容。",13)); panel.Children.Add(Card(performance));
        var diagnostics=Stack(10); diagnostics.Children.Add(Text("本次会话",18,true));
        diagnostics.Children.Add(Text($"模板首屏：{timings.GetValueOrDefault("firstPageReady"):0} ms · 本地服务：{timings.GetValueOrDefault("backendReady"):0} ms\n编辑预览实例：{webInitializations} · 整页加载：{previewNavigations} · 文档更新：{previewPatches}",13));
        diagnostics.Children.Add(Text("耗时从窗口创建起计算，供比较同机启动表现；不包含启动进程前的磁盘与系统调度时间。",11)); panel.Children.Add(Card(diagnostics)); FillScroll(panel);
    }
}
