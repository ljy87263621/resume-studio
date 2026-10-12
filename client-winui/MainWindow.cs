using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace ResumeStudio;

public sealed partial class MainWindow : Window
{
    private readonly BackendClient api = new();
    private readonly Grid shell = new();
    private readonly Grid body = new();
    private readonly TextBlock pageTitle = Text("模板中心",28,true);
    private readonly TextBlock pageSubtitle = Text("从一份清晰的简历，开始下一段旅程。",13);
    private readonly TextBlock saveState = Text("正在连接本地服务…",12);
    private readonly InfoBar notice = new() { IsClosable=true, Margin=new Thickness(0,0,0,12) };
    private readonly List<Button> navigation = [];
    private readonly DispatcherTimer editTimer = new() { Interval=TimeSpan.FromMilliseconds(650) };
    private readonly SemaphoreSlim saveLock = new(1,1);
    private List<Template> templates = [];
    private Resume? current;
    private WebView2? preview;
    private bool dirty, closing, ready;
    private int revision;
    private string currentPage="templates";
    private string jobKeyword="",jobCity="",jobProvider="all";
    private int jobPage=1;
    private bool searching;
    private string renderMarker="";

    public MainWindow()
    {
        Title="拾简 Resume Studio";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,960));
        SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();
        shell.RequestedTheme=ElementTheme.Light;
        shell.Background=Brush("#F5F7FB");
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(222) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        BuildSidebar();
        var content=new Grid { Margin=new Thickness(30,26,30,22) };
        content.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        var header=new Grid { Margin=new Thickness(0,0,0,22) };
        var heading=Stack(5); heading.Children.Add(pageTitle); heading.Children.Add(pageSubtitle); header.Children.Add(heading);
        saveState.HorizontalAlignment=HorizontalAlignment.Right; saveState.VerticalAlignment=VerticalAlignment.Center; saveState.Foreground=Brush("#167D78"); header.Children.Add(saveState);
        content.Children.Add(header); Grid.SetRow(notice,1); content.Children.Add(notice); Grid.SetRow(body,2); content.Children.Add(body); Grid.SetColumn(content,1); shell.Children.Add(content);
        Content=shell;
        shell.Loaded+=async (_,_)=>await Run(Initialize);
        editTimer.Tick+=async (_,_)=> { editTimer.Stop(); await Run(async ()=> { await RenderPreview(); await SaveCurrent(); }); };
        AppWindow.Closing+=(sender,args)=>
        {
            if(closing) return;
            args.Cancel=true;
            _=Run(async ()=> { editTimer.Stop(); await SaveCurrent(); closing=true; api.Dispose(); Close(); });
        };
    }
    private static SolidColorBrush Brush(string hex)
    {
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255,Convert.ToByte(hex.Substring(1,2),16),Convert.ToByte(hex.Substring(3,2),16),Convert.ToByte(hex.Substring(5,2),16)));
    }
    private static TextBlock Text(string text,double size=14,bool bold=false) => new() { Text=text,FontSize=size,FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal,Foreground=Brush("#22304A"),TextWrapping=TextWrapping.Wrap };
    private static StackPanel Stack(double spacing=12) => new() { Spacing=spacing };
    private static StackPanel Row(double spacing=10) => new() { Orientation=Orientation.Horizontal,Spacing=spacing };
    private static Border Card(UIElement child,Thickness? padding=null) => new() { Child=child,Padding=padding??new Thickness(22),Background=Brush("#FFFFFF"),BorderBrush=Brush("#E3E8F1"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12) };
    private Button Button(string label,Func<Task> action,bool primary=false)
    {
        var b=new Button { Content=label,Padding=new Thickness(15,9,15,9),CornerRadius=new CornerRadius(7) };
        if(primary) { b.Background=Brush("#245AE8"); b.Foreground=Brush("#FFFFFF"); }
        b.Click+=async (_,_)=>await Run(action); return b;
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch(Exception ex) { notice.Title="操作未完成"; notice.Message=ex.Message; notice.Severity=InfoBarSeverity.Error; notice.IsOpen=true; }
    }
    private async Task Initialize()
    {
        if(ready) return;
        var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var scale=shell.XamlRoot.RasterizationScale;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min((int)(1440*scale),area.Width-80),Math.Min((int)(920*scale),area.Height-60)));
        await api.StartAsync(); templates=await api.Get<List<Template>>("templates"); ready=true; saveState.Text="本地服务已连接";
        await Navigate("templates");
        if(Environment.GetCommandLineArgs().Contains("--self-test"))
        {
            Title="拾简 · 自动验证窗口"; body.IsHitTestVisible=false;
            foreach(var button in navigation) button.IsEnabled=false;
            try { await SelfTest(); }
            finally { body.IsHitTestVisible=true; foreach(var button in navigation) button.IsEnabled=true; }
        }
    }
    private void BuildSidebar()
    {
        var side=new Grid { Background=Brush("#14233F"),Padding=new Thickness(17,28,17,20) };
        side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); side.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) }); side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var brand=Stack(5); var name=Text("拾简",30,true); name.Foreground=Brush("#FFFFFF"); brand.Children.Add(name); var sub=Text("RESUME STUDIO",10); sub.CharacterSpacing=150; sub.Foreground=Brush("#8CA6D2"); brand.Children.Add(sub); side.Children.Add(brand);
        var menu=Stack(7); menu.Margin=new Thickness(0,35,0,0);
        foreach(var item in new[] {("templates","▦   模板中心"),("resumes","▤   我的简历"),("jobs","⌕   发现职位"),("sources","◎   公司招聘官网"),("favorites","☆   职位收藏"),("applications","✓   投递记录"),("about","ⓘ   使用说明")})
        {
            var key=item.Item1;
            var b=Button(item.Item2,async ()=> { if(ready) await Navigate(key); }); b.Tag=key; b.HorizontalAlignment=HorizontalAlignment.Stretch; b.HorizontalContentAlignment=HorizontalAlignment.Left; b.Foreground=Brush("#CBD7EB"); b.Background=new SolidColorBrush(Colors.Transparent); b.BorderThickness=new Thickness(0); b.Padding=new Thickness(14,13,14,13); navigation.Add(b); menu.Children.Add(b);
        }
        Grid.SetRow(menu,1); side.Children.Add(menu);
        var footer=Stack(8); var tagline=Text("把经历写清楚\n让机会更靠近",13); tagline.Foreground=Brush("#A9BEDC"); footer.Children.Add(tagline); var v=Text("v0.3.0  ·  Windows 桌面版",10); v.Foreground=Brush("#7189AE"); footer.Children.Add(v); Grid.SetRow(footer,2); side.Children.Add(footer); shell.Children.Add(side);
    }
    private async Task Navigate(string page)
    {
        editTimer.Stop(); await SaveCurrent();
        currentPage=page; notice.IsOpen=false;
        if(preview is not null) { preview.Close(); preview=null; }
        body.Children.Clear(); body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
        foreach(var b in navigation) b.Background=Brush((string)b.Tag==page?"#294267":"#14233F");
        switch(page)
        {
            case "templates": ShowTemplates(); break;
            case "resumes": await ShowResumes(); break;
            case "jobs": await ShowJobs(); break;
            case "sources": await ShowCompanySources(); break;
            case "favorites": await ShowFavorites(); break;
            case "applications": await ShowApplications(); break;
            default: ShowAbout(); break;
        }
    }
    private void Heading(string title,string subtitle) { pageTitle.Text=title; pageSubtitle.Text=subtitle; }
    private void FillScroll(UIElement child)
    {
        var scroll=new ScrollViewer { Content=child,HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        scroll.SizeChanged+=(_,e)=> { if(child is FrameworkElement element) element.Width=Math.Max(0,e.NewSize.Width-16); };
        body.Children.Add(scroll);
    }
    private async Task CreateResume(Template t)
    {
        await SaveCurrent(); current=await api.Save("resumes",Blank(t)); dirty=false; await OpenEditor();
    }
    private async Task ShowResumes()
    {
        Heading("我的简历","每个求职方向都可以有一份独立简历，编辑时自动保存。");
        var panel=Stack(18); var toolbar=Row(); toolbar.Children.Add(Button("＋ 新建简历",()=>Navigate("templates"),true)); toolbar.Children.Add(Button("导入备份",ImportResume)); panel.Children.Add(toolbar);
        var list=await api.Get<List<Resume>>("resumes");
        if(list.Count==0) panel.Children.Add(Card(Text("还没有简历。从模板中心选择一套模板，开始填写你的经历。",16)));
        foreach(var r in list)
        {
            var c=Stack(12); c.Children.Add(Text(r.Title,20,true)); c.Children.Add(Text($"{r.Profile.Name}  ·  {r.Profile.Role}  ·  {templates.First(t=>t.Id==r.TemplateId).Name}",13)); c.Children.Add(Text("最近保存："+LocalDate(r.UpdatedAt),11));
            var actions=Row(); actions.Children.Add(Button("继续编辑",async ()=> { current=r; dirty=false; await OpenEditor(); },true)); actions.Children.Add(Button("复制",async ()=> { var clone=JsonSerializer.Deserialize<Resume>(JsonSerializer.Serialize(r,BackendClient.Json),BackendClient.Json)!; clone.Id=Guid.NewGuid().ToString(); clone.Title+=" · 副本"; await api.Save("resumes",clone); await Navigate("resumes"); })); actions.Children.Add(Button("删除",async ()=> { if(await Confirm("删除这份简历？",r.Title+"，删除后无法在应用内恢复。")) { await api.Delete("resumes/"+r.Id); if(current?.Id==r.Id) current=null; await Navigate("resumes"); } })); c.Children.Add(actions); panel.Children.Add(Card(c));
        }
        FillScroll(panel);
    }
    private async Task OpenEditor()
    {
        if(current is null) return;
        if(preview is not null) { preview.Close(); preview=null; }
        currentPage="editor"; body.Children.Clear(); body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
        Heading("简历工作台","左侧编辑内容，右侧实时预览。所有修改会自动保存到本机。");
        foreach(var b in navigation) b.Background=Brush((string)b.Tag=="resumes"?"#294267":"#14233F");
        body.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(410) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(20) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var toolbar=Row(8); toolbar.Margin=new Thickness(0,0,0,15); toolbar.Children.Add(Button("← 我的简历",()=>Navigate("resumes"))); toolbar.Children.Add(Button("保存",SaveCurrent)); toolbar.Children.Add(Button("导出 PDF",ExportPdf,true)); toolbar.Children.Add(Button("备份 JSON",ExportJson)); toolbar.Children.Add(Button("查看打印版",async ()=> { await RenderPreview(); if(preview?.CoreWebView2 is not null) preview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); })); Grid.SetColumnSpan(toolbar,3); body.Children.Add(toolbar);
        toolbar.Children.Add(Button("检查与建议",ShowResumeCheck)); toolbar.Children.Add(Button("AI 提示词",CopyAiPrompt));
        var form=Stack(16);
        var basic=Stack(12); basic.Children.Add(Text("基本信息",18,true));
        basic.Children.Add(Field("简历名称",current.Title,v=>current.Title=v));
        basic.Children.Add(Field("姓名",current.Profile.Name,v=>current.Profile.Name=v)); basic.Children.Add(Field("求职意向",current.Profile.Role,v=>current.Profile.Role=v));
        basic.Children.Add(Field("电话",current.Profile.Phone,v=>current.Profile.Phone=v)); basic.Children.Add(Field("邮箱",current.Profile.Email,v=>current.Profile.Email=v)); basic.Children.Add(Field("城市",current.Profile.City,v=>current.Profile.City=v)); basic.Children.Add(Field("个人主页 / 作品链接",current.Profile.Link,v=>current.Profile.Link=v)); basic.Children.Add(Field("个人简介",current.Profile.Summary,v=>current.Profile.Summary=v,true));
        var photo=Row(); photo.Children.Add(Button("添加证件照",ImportPhoto)); photo.Children.Add(Button("移除照片",()=> { current.Photo=""; Changed(); return Task.CompletedTask; })); basic.Children.Add(photo); form.Children.Add(Card(basic,new Thickness(18)));
        var style=Stack(12); style.Children.Add(Text("排版设置",18,true));
        var templateBox=new ComboBox { Header="模板",ItemsSource=templates,SelectedItem=templates.First(t=>t.Id==current.TemplateId),HorizontalAlignment=HorizontalAlignment.Stretch };
        templateBox.SelectionChanged+=(_,_)=> { if(templateBox.SelectedItem is Template t) { current.TemplateId=t.Id; current.Color=t.Color; Changed(); } }; style.Children.Add(templateBox);
        var colors=Row(8); foreach(string color in new[] {"#245AE8","#167D78","#353D59","#222222","#7955B4","#1E668A"}) { var c=Button("●",()=> { current.Color=color; Changed(); return Task.CompletedTask; }); c.Foreground=Brush(color); c.Padding=new Thickness(9,5,9,5); colors.Children.Add(c); } style.Children.Add(colors);
        var size=new Slider { Header="正文字号",Minimum=10,Maximum=16,StepFrequency=0.5,Value=current.FontSize }; size.ValueChanged+=(_,_)=> { current.FontSize=size.Value; Changed(); }; style.Children.Add(size);
        var spacing=new Slider { Header="行间距",Minimum=1.2,Maximum=2,StepFrequency=0.1,Value=current.LineHeight }; spacing.ValueChanged+=(_,_)=> { current.LineHeight=spacing.Value; Changed(); }; style.Children.Add(spacing); form.Children.Add(Card(style,new Thickness(18)));
        var modules=Stack(12); modules.Children.Add(Text("内容模块",18,true)); modules.Children.Add(Text("支持：## 小标题 | 时间、- 列表、**加粗**、Markdown 表格。点击检查与建议查看完成状态和岗位填写重点。",11));
        foreach(var section in current.Sections)
        {
            var block=Stack(10); block.Children.Add(Field("模块标题",section.Title,v=>section.Title=v)); block.Children.Add(Field("内容",section.Content,v=>section.Content=v,true,"## 项目 / 经历名称\n时间 · 角色\n- 使用的技术、承担的职责与成果"));
            var actions=Row(5); var visible=new ToggleSwitch { Header="显示模块",IsOn=section.Visible }; visible.Toggled+=(_,_)=> { section.Visible=visible.IsOn; Changed(); }; actions.Children.Add(visible);
            actions.Children.Add(Button("↑",async ()=> { int i=current.Sections.IndexOf(section); if(i>0) { current.Sections.RemoveAt(i); current.Sections.Insert(i-1,section); Changed(); await SaveCurrent(); await OpenEditor(); } }));
            actions.Children.Add(Button("↓",async ()=> { int i=current.Sections.IndexOf(section); if(i<current.Sections.Count-1) { current.Sections.RemoveAt(i); current.Sections.Insert(i+1,section); Changed(); await SaveCurrent(); await OpenEditor(); } }));
            actions.Children.Add(Button("移除",async ()=> { if(await Confirm("移除模块？",section.Title)) { current.Sections.Remove(section); Changed(); await SaveCurrent(); await OpenEditor(); } })); block.Children.Add(actions); modules.Children.Add(Card(block,new Thickness(15)));
        }
        modules.Children.Add(Button("＋ 添加模块",async ()=> { current.Sections.Add(new ResumeSection { Title="自定义模块" }); Changed(); await SaveCurrent(); await OpenEditor(); })); form.Children.Add(modules);
        var scroll=new ScrollViewer { Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; Grid.SetRow(scroll,1); body.Children.Add(scroll);
        preview=new WebView2 { HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch };
        var previewPanel=new Grid(); previewPanel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); previewPanel.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        capacityText=Text("正在估算 A4 内容容量…",11); capacityText.Margin=new Thickness(12,9,12,9); previewPanel.Children.Add(capacityText); Grid.SetRow(preview,1); previewPanel.Children.Add(preview);
        var frame=Card(previewPanel,new Thickness(0)); Grid.SetColumn(frame,2); Grid.SetRow(frame,1); body.Children.Add(frame);
        await InitializeWeb(preview); await RenderPreview();
    }
    private TextBox Field(string label,string value,Action<string> setter,bool multiline=false,string placeholder="")
    {
        var box=new TextBox { Header=label,Text=value,PlaceholderText=placeholder,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?112:35,MaxLength=multiline?30000:300 };
        box.TextChanged+=(_,_)=> { setter(box.Text.Replace("\r\n","\n").Replace('\r','\n')); Changed(); }; return box;
    }
    private void Changed() { dirty=true; revision++; saveState.Text="有未保存的修改…"; editTimer.Stop(); editTimer.Start(); }
    private async Task SaveCurrent()
    {
        await saveLock.WaitAsync();
        try
        {
            while(current is not null&&dirty)
            {
                int savedRevision=revision; string id=current.Id;
                var snapshot=JsonSerializer.Deserialize<Resume>(JsonSerializer.Serialize(current,BackendClient.Json),BackendClient.Json)!;
                if(string.IsNullOrWhiteSpace(snapshot.Title)) { saveState.Text="请填写简历名称后保存"; throw new InvalidOperationException("简历名称不能为空，当前修改尚未保存。"); }
                var saved=await api.Save("resumes",snapshot);
                if(current.Id==id&&revision==savedRevision) { dirty=false; current.UpdatedAt=saved.UpdatedAt; saveState.Text="已保存  "+DateTime.Now.ToString("HH:mm:ss"); }
            }
        }
        finally { saveLock.Release(); }
    }
    private async Task InitializeWeb(WebView2 web)
    {
        var environment=await CoreWebView2Environment.CreateWithOptionsAsync("",Path.Combine(api.DataDirectory,"WebView2"),new CoreWebView2EnvironmentOptions());
        await web.EnsureCoreWebView2Async(environment);
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false; web.CoreWebView2.Settings.AreDevToolsEnabled=false;
        web.CoreWebView2.NewWindowRequested+=(_,e)=>e.Handled=true;
        web.CoreWebView2.NavigationStarting+=(_,e)=>
        {
            // WinUI's wrapper implements NavigateToString through a data:text/html URL.
            if(!e.Uri.StartsWith("about:",StringComparison.OrdinalIgnoreCase)&&!e.Uri.StartsWith("data:text/html",StringComparison.OrdinalIgnoreCase)) e.Cancel=true;
            if(Environment.GetCommandLineArgs().Contains("--self-test")) File.AppendAllText(Path.Combine(api.DataDirectory,"preview-debug.log"),$"starting {e.Uri.Split(',')[0]} cancelled={e.Cancel}\n");
        };
        web.CoreWebView2.NavigationCompleted+=(sender,e)=>
        {
            if(Environment.GetCommandLineArgs().Contains("--self-test")) File.AppendAllText(Path.Combine(api.DataDirectory,"preview-debug.log"),$"completed {e.IsSuccess} {e.WebErrorStatus}\n");
            if(e.IsSuccess&&web==preview) _=UpdateCapacity(web);
        };
    }
    private Task RenderPreview()
    {
        if(current is not null&&preview?.CoreWebView2 is not null)
        {
            renderMarker=Guid.NewGuid().ToString("N");
            string html=ResumeRenderer.Html(current,templates.First(t=>t.Id==current.TemplateId));
            preview.NavigateToString(html.Replace("<head>",$"<head><meta name='resume-render-id' content='{renderMarker}'>"));
        }
        return Task.CompletedTask;
    }
    private async Task WaitForPreview()
    {
        if(preview?.CoreWebView2 is null) throw new InvalidOperationException("预览尚未就绪");
        var web=preview; var core=web.CoreWebView2;
        editTimer.Stop(); await RenderPreview();
        for(int i=0;i<120;i++)
        {
            if(web!=preview) throw new OperationCanceledException("已切换页面，请返回工作台重新导出。");
            var loaded=await core.ExecuteScriptAsync("document.readyState === 'complete' ? document.querySelector('meta[name=resume-render-id]')?.content : null");
            if(loaded==JsonSerializer.Serialize(renderMarker)) return;
            await Task.Delay(100);
        }
        var diagnostic=await core.ExecuteScriptAsync("JSON.stringify({state:document.readyState,meta:document.querySelector('meta[name=resume-render-id]')?.content})");
        throw new IOException("预览加载超时："+diagnostic);
    }
    private async Task ExportPdf()
    {
        if(current is null) return; await SaveCurrent();
        var picker=new FileSavePicker { SuggestedFileName=SafeName(current.Title) }; picker.FileTypeChoices.Add("PDF 文档",new List<string> {".pdf"}); InitPicker(picker); var file=await picker.PickSaveFileAsync(); if(file is null) return;
        await PrintPdf(file.Path); Success("PDF 已导出",file.Path);
    }
    private async Task PrintPdf(string path)
    {
        await WaitForPreview(); var settings=preview!.CoreWebView2.Environment.CreatePrintSettings(); settings.ShouldPrintBackgrounds=true; settings.ShouldPrintHeaderAndFooter=false; settings.PageWidth=8.2677; settings.PageHeight=11.6929; settings.MarginTop=0; settings.MarginBottom=0; settings.MarginLeft=0; settings.MarginRight=0;
        if(!await preview.CoreWebView2.PrintToPdfAsync(path,settings)) throw new IOException("PDF 导出失败，请检查目标文件是否被占用。");
    }
    private async Task ExportJson()
    {
        if(current is null) return; await SaveCurrent(); var picker=new FileSavePicker { SuggestedFileName=SafeName(current.Title) }; picker.FileTypeChoices.Add("简历备份",new List<string> {".json"}); InitPicker(picker); var file=await picker.PickSaveFileAsync(); if(file is null) return;
        await File.WriteAllTextAsync(file.Path,JsonSerializer.Serialize(current,BackendClient.Json)); Success("备份已导出",file.Path);
    }
    private async Task ImportResume()
    {
        var picker=new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); InitPicker(picker); var file=await picker.PickSingleFileAsync(); if(file is null) return;
        if(new FileInfo(file.Path).Length>2_000_000) throw new IOException("备份不能超过 2 MB");
        var r=JsonSerializer.Deserialize<Resume>(await File.ReadAllTextAsync(file.Path),BackendClient.Json)??throw new IOException("无法识别简历备份");
        if(r.Profile is null||r.Sections is null||r.Sections.Any(s=>s is null)||templates.All(t=>t.Id!=r.TemplateId)) throw new IOException("备份内容或模板无效");
        r.Id=Guid.NewGuid().ToString(); r.Title+=" · 导入"; current=await api.Save("resumes",r); dirty=false; await OpenEditor();
    }
    private async Task ImportPhoto()
    {
        var picker=new FileOpenPicker(); picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg"); InitPicker(picker); var file=await picker.PickSingleFileAsync(); if(file is null||current is null) return;
        if(new FileInfo(file.Path).Length>600_000) throw new IOException("请选择小于 600 KB 的 PNG/JPEG 照片。");
        var bytes=await File.ReadAllBytesAsync(file.Path); bool png=bytes.Length>8&&bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}); bool jpeg=bytes.Length>3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255;
        if(!png&&!jpeg) throw new IOException("文件不是有效的 PNG/JPEG 图片。"); current.Photo="data:image/"+(png?"png":"jpeg")+";base64,"+Convert.ToBase64String(bytes); Changed();
    }
    private void InitPicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
    private static string SafeName(string text) => string.Join("_",text.Split(Path.GetInvalidFileNameChars(),StringSplitOptions.RemoveEmptyEntries));
    private async Task<bool> Confirm(string title,string message) => await new ContentDialog { XamlRoot=shell.XamlRoot,Title=title,Content=message,PrimaryButtonText="确认",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close }.ShowAsync()==ContentDialogResult.Primary;
    private void Success(string title,string message) { notice.Title=title; notice.Message=message; notice.Severity=InfoBarSeverity.Success; notice.IsOpen=true; }
    private static string LocalDate(string value) => DateTimeOffset.TryParse(value,out var dt)?dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):value;

    private Border JobCard(Job j,bool favorite)
    {
        var c=Stack(12); c.Children.Add(Text(j.Title,20,true)); c.Children.Add(Text($"{j.Company}  ·  {j.Location}  ·  {j.Experience}",13)); c.Children.Add(Text($"{j.Source}  ·  {(string.IsNullOrEmpty(j.PublishedAt)?j.InfoStatus:"更新："+j.PublishedAt)}  ·  {j.Salary}",11));
        if(!string.IsNullOrWhiteSpace(j.Notes)) c.Children.Add(Text("备注："+j.Notes,12));
        var description=Text(j.Description,13); description.MaxHeight=145; description.TextTrimming=TextTrimming.CharacterEllipsis; c.Children.Add(description);
        var actions=Row(); actions.Children.Add(Button("详情",async ()=> { var content=Stack(); content.Children.Add(Text(j.Company+" · "+j.Location,14,true)); content.Children.Add(Text(j.Description,14)); var dialog=new ContentDialog { XamlRoot=shell.XamlRoot,Title=j.Title,Content=new ScrollViewer { Content=content,MaxHeight=480 },PrimaryButtonText="前往投递",CloseButtonText="返回" }; if(await dialog.ShowAsync()==ContentDialogResult.Primary) await OpenJob(j); })); actions.Children.Add(Button("前往投递 ↗",()=>OpenJob(j),true));
        actions.Children.Add(Button(favorite?"取消收藏":"收藏职位",async ()=> { if(favorite) { await api.Delete("favorites/"+j.Id); await Navigate("favorites"); } else { await api.Save("favorites",j); Success("已收藏",j.Title); } })); if(favorite) actions.Children.Add(Button("编辑收藏",()=>EditFavorite(j,false))); c.Children.Add(actions); return Card(c);
    }
    private async Task OpenJob(Job job)
    {
        await Launch(job.Url);
        var records=await api.Get<List<ApplicationRecord>>("applications");
        if(records.All(r=>r.JobId!=job.Id)) await api.Save("applications",new ApplicationRecord { JobId=job.Id,Title=job.Title,Company=job.Company,Url=job.Url,ResumeTitle=current?.Title??"" });
        Success("已打开招聘页面","记录状态为“已打开”。完成原站投递后，可在投递记录中更新为“已投递”。");
    }
    private static async Task Launch(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||string.IsNullOrEmpty(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo)) throw new IOException("招聘链接无效");
        if(!await Windows.System.Launcher.LaunchUriAsync(uri)) throw new IOException("无法打开默认浏览器");
    }
    private async Task ShowFavorites()
    {
        Heading("职位收藏","跨平台收藏到拾简，保留原始链接、职位描述和个人备注。"); var panel=Stack(16); var list=await api.Get<List<Job>>("favorites");
        panel.Children.Add(Button("＋ 从 BOSS 等平台链接收藏",ImportExternalFavorite,true));
        var filters=Row(); var query=new TextBox { PlaceholderText="职位 / 公司 / 备注",Width=280 }; var source=new ComboBox { ItemsSource=new[] {"全部平台"}.Concat(list.Select(j=>j.Source).Distinct()).ToList(),SelectedIndex=0,Width=180 }; filters.Children.Add(query); filters.Children.Add(source); panel.Children.Add(filters);
        var cards=Stack(16); void Refresh() { cards.Children.Clear(); var found=list.Where(j=>(j.Title+" "+j.Company+" "+j.Notes).Contains(query.Text,StringComparison.OrdinalIgnoreCase)&&((string)source.SelectedItem=="全部平台"||j.Source==(string)source.SelectedItem)).ToList(); if(found.Count==0) cards.Children.Add(Card(Text("没有匹配的收藏。可从招聘平台复制职位详情链接添加。",16))); foreach(var j in found) cards.Children.Add(JobCard(j,true)); }
        query.TextChanged+=(_,_)=>Refresh(); source.SelectionChanged+=(_,_)=>Refresh(); Refresh(); panel.Children.Add(cards); FillScroll(panel);
    }
    private async Task ShowApplications()
    {
        Heading("投递记录","记录已打开、已投递、面试和录用进度。状态由你确认更新。"); var panel=Stack(18);
        var records=await api.Get<List<ApplicationRecord>>("applications");
        var stats=Row(16); foreach(string s in new[] {"已打开","已投递","面试中","已录用","已结束"}) stats.Children.Add(Card(Text($"{s}  {records.Count(r=>r.Status==s)}",15,true),new Thickness(16))); panel.Children.Add(stats);
        panel.Children.Add(Button("＋ 手动添加投递",async ()=> { var record=new ApplicationRecord { Status="已投递" }; var f=Stack(); var title=new TextBox { Header="职位名称" }; var company=new TextBox { Header="公司" }; var url=new TextBox { Header="招聘页面链接（HTTPS）" }; f.Children.Add(title); f.Children.Add(company); f.Children.Add(url); var dialog=new ContentDialog { XamlRoot=shell.XamlRoot,Title="添加投递记录",Content=f,PrimaryButtonText="保存",CloseButtonText="取消" }; if(await dialog.ShowAsync()==ContentDialogResult.Primary) { record.Title=title.Text; record.Company=company.Text; record.Url=url.Text; await api.Save("applications",record); await Navigate("applications"); } }));
        if(records.Count==0) panel.Children.Add(Card(Text("打开职位投递页面后，这里会自动生成一条“已打开”记录。",16)));
        foreach(var r in records)
        {
            var c=Stack(12); c.Children.Add(Text(r.Title,20,true)); c.Children.Add(Text(r.Company+"  ·  创建时间："+LocalDate(r.CreatedAt),12)); var line=Row();
            var status=new ComboBox { Header="当前进度",ItemsSource=new[] {"已打开","已投递","面试中","已录用","已结束"},SelectedItem=r.Status,Width=150 };
            var resume=new TextBox { Header="使用的简历版本",Text=r.ResumeTitle,Width=280 }; line.Children.Add(status); line.Children.Add(resume); c.Children.Add(line);
            var notes=new TextBox { Header="备注 / 面试安排",Text=r.Notes,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=85,MaxLength=10000 }; c.Children.Add(notes);
            var actions=Row(); actions.Children.Add(Button("保存记录",async ()=> { r.Status=(string)status.SelectedItem; r.ResumeTitle=resume.Text; r.Notes=notes.Text; await api.Save("applications",r); await Navigate("applications"); },true)); actions.Children.Add(Button("打开原页面 ↗",()=>Launch(r.Url))); actions.Children.Add(Button("删除",async ()=> { if(await Confirm("删除记录？",r.Title)) { await api.Delete("applications/"+r.Id); await Navigate("applications"); } })); c.Children.Add(actions); panel.Children.Add(Card(c));
        }
        FillScroll(panel);
    }
    private void ShowAbout()
    {
        Heading("使用说明","简历制作与求职跟进，在一个桌面工作区完成。"); var content=Stack(22);
        foreach(var item in new[] {("01  GitHub 高星模板","8 套岗位适配，覆盖前端、后端、嵌入式与通用型，来自 Awesome-CV、billryan/resume、Deedy-Resume 和 Jake’s Resume。卡片显示仓库来源、核实星数和许可。版式从原仓库 LaTeX 模板移植为中文 A4，原文件与许可随应用保存。6 套旧模板保留以兼容已有简历。"),("02  制作与辅助","填写信息与经历 → 调整模块顺序和样式 → 检查模块状态与规则建议 → 查看 A4 内容余量 → 导出 PDF。支持 Markdown 表格与标题日期。AI 提示词按钮只复制规则与当前简历，不连接在线模型。"),("03  招聘与跨平台收藏","21个公司来源可在发现职位汇总查询，支持腾讯及 Greenhouse、Lever、Ashby、SmartRecruiters、Workday；可添加国内外公司官网并查看真实接入状态。BOSS、猎聘、智联、前程无忧、拉勾、牛客和企业官网职位可复制详情链接收藏到拾简，补充描述、薪资与备注，并按平台筛选。相同链接的跟踪参数会归一化去重。平台账号收藏不会自动同步。"),("04  直接采集的范围","当前自动获取的是公开职位源。BOSS 搜索页依赖脚本和访问状态，robots 限制搜索参数路径，当前未实现其匿名批量采集；可正常打开原站登录浏览后复制职位链接和描述。未实现验证码绕过、Cookie 读取或自动投递。"),("05  投递与数据","“前往投递”打开原页面并记录“已打开”；实际投递在原站完成，再手动更新进度。简历、收藏、投递记录由本地 Java 服务保存。上一版文档保留 .bak，JSON 备份含个人信息。"),("06  技术与来源","客户端为原生 WinUI 3 / .NET 8，后端 Java 21，WebView2 用于预览和 PDF。开源模板与 resume-helper 功能来源说明见安装目录 THIRD_PARTY_NOTICES.md。")}) { var s=Stack(); s.Children.Add(Text(item.Item1,20,true)); s.Children.Add(Text(item.Item2,15)); content.Children.Add(Card(s)); }
        content.Children.Add(Card(Text("本机数据目录：\n"+api.DataDirectory,13))); content.Children.Add(Button("打开数据目录",()=> { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList={api.DataDirectory},UseShellExecute=true }); return Task.CompletedTask; })); FillScroll(content);
    }
    private async Task SelfTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"verification"); Directory.CreateDirectory(output);
        try
        {
            var r=Demo(templates[0]); r.Title="自动验证简历"; r.Profile.Name="桌面链路验证";
            current=await api.Save("resumes",r); dirty=false; await OpenEditor();
            TextBox? FindField(DependencyObject parent,string header)
            {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
                {
                    var child=VisualTreeHelper.GetChild(parent,i);
                    if(child is TextBox box&&box.Header?.ToString()==header) return box;
                    var found=FindField(child,header); if(found is not null) return found;
                }
                return null;
            }
            // Editing the actual native TextBox exercises TextChanged and the automatic save timer.
            await Task.Delay(300);
            var summary=FindField(shell,"个人简介")??throw new Exception("Native editor field not found");
            summary.Text="<script>alert('x')</script>\n测试 **加粗** 与中文导出。";
            await Task.Delay(200); // WinUI dispatches native TextChanged after this assignment returns.
            for(int i=0;i<30&&dirty;i++) await Task.Delay(100);
            if(dirty) throw new Exception("Automatic save did not complete");
            var saved=(await api.Get<List<Resume>>("resumes")).First(x=>x.Id==r.Id); if(saved.Profile.Name!="桌面链路验证"||!saved.Profile.Summary.Contains("<script>")) throw new Exception("Persistence test failed");
            await PrintPdf(Path.Combine(output,"resume-preview.pdf"));
            var html=ResumeRenderer.Html(current,templates[0]); if(html.Contains("<script>")) throw new Exception("HTML escape test failed"); await File.WriteAllTextAsync(Path.Combine(output,"resume-preview.html"),html);
            var advice=await api.Post<ResumeCheck>("resume-check",current); if(advice.Modules.Count!=current.Sections.Count+1) throw new Exception("Module advice test failed");
            var copiedLink=new TextBox { AcceptsReturn=true,Text="测试职位（收藏链路验证）\nhttps://www.zhipin.com/job_detail/acceptance123.html?ka=share" };
            var external=await api.Post<CapturePreview>("capture-preview",new {text=copiedLink.Text});
            if(external.Job.Source!="BOSS直聘"||external.Job.Title!="测试职位（收藏链路验证）") throw new Exception("Native shared-link parsing failed");
            await api.Save("favorites",external.Job);
            var duplicate=await api.Post<CapturePreview>("capture-preview",new {text="https://www.zhipin.com/job_detail/acceptance123.html?utm_source=other"});
            if(duplicate.Job.Id!=external.Job.Id) throw new Exception("External favorite deduplication failed");
            current.Sections[0].Content+="\n| 项目 | 内容 |\n| --- | --- |\n| 验证 | 中文表格 |";
            if(!ResumeRenderer.Html(current,templates[0]).Contains("<table>")) throw new Exception("Markdown table failed");
            var original=current;
            foreach(var template in templates)
            {
                current=Demo(template); await PrintPdf(Path.Combine(output,"template-"+template.Id+".pdf"));
            }
            current=Demo(templates[0]); current.Sections[2].Content=string.Join("\n",Enumerable.Range(1,70).Select(i=>$"- 多页排版验证 {i}：这是一段用于检查中文长内容跨页边距和换行的示例内容。"));
            await PrintPdf(Path.Combine(output,"multipage-resume.pdf")); current=original; await RenderPreview();
            var jobs=await api.Get<JobResult>("jobs?provider=tencent&keyword=Java&page=1"); if(jobs.Status!="ok"||jobs.Items.Count==0) throw new Exception("Live jobs unavailable: "+jobs.Message);
            var job=jobs.Items[0]; await api.Save("favorites",job); var favorite=(await api.Get<List<Job>>("favorites")).First(x=>x.Id==job.Id); if(favorite.Url!=job.Url) throw new Exception("Favorites test failed");
            var record=await api.Save("applications",new ApplicationRecord { Title=job.Title,Company=job.Company,JobId=job.Id,Url=job.Url }); record.Status="面试中"; record.Notes="测试记录"; await api.Save("applications",record); if((await api.Get<List<ApplicationRecord>>("applications")).First(x=>x.Id==record.Id).Status!="面试中") throw new Exception("Application test failed");
            await Navigate("jobs"); if(companySearch is null) throw new Exception("Company query UI did not initialize"); await companySearch(1);
            if(shownCompanyJobs is null||shownCompanyJobs.Sources.Count!=21||shownCompanyJobs.Items.Select(x=>x.Company).Distinct().Count()<3) throw new Exception("Multi-company native UI query failed");
            try { await SaveNativePreview(Path.Combine(output,"multi-company-jobs.png")); } catch(Exception) { /* Optional preview capture may be unavailable for a minimized window. */ }
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new { status="passed",savedResumeId=r.Id,jobCount=jobs.Items.Count,jobSource="腾讯招聘",sampleJobUrl=job.Url,templateCount=templates.Count,githubTemplateCount=templates.Count(t=>t.Origin=="github"),companySourceCount=shownCompanyJobs.Sources.Count,aggregateJobCount=shownCompanyJobs.Items.Count,companies=shownCompanyJobs.Items.Select(x=>x.Company).Distinct().ToList(),pdfBytes=new FileInfo(Path.Combine(output,"resume-preview.pdf")).Length,checks=new[] {"WinUI launch","Java startup","native TextBox editing and auto-save","resume create/update/read","WebView preview","PDF export","all 14 template PDFs","multipage PDF","HTML escaping","live recruitment","favorites persistence","application status update","module statuses and suggestions","native shared-link parsing","BOSS link local collection","tracking URL deduplication","Markdown tables","company registry native UI","multi-employer aggregation native UI"} },BackendClient.Json));
            saveState.Text="桌面链路验证通过";
        }
        catch(Exception ex) { await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {status="failed",error=ex.ToString()},BackendClient.Json)); throw; }
    }
}
