using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using System.Diagnostics;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

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
    private readonly DispatcherTimer previewTimer = new();
    private readonly SemaphoreSlim saveLock = new(1,1);
    private List<Template> templates = [];
    private Resume? current;
    private WebView2? preview;
    private bool dirty, closing, ready;
    private int revision;
    private string currentPage="templates";
    private string jobKeyword="",jobCity="",jobProvider="all";
    private int jobPage=1;
    private int navigationRevision;
    private string renderMarker="";

    public MainWindow()
    {
        Title="拾简 Resume Studio";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,960));
        SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();
        preferences=AppPreferences.Load(api.DataDirectory); BuildContentHost(); BuildShell();
        if(Environment.GetCommandLineArgs().Contains("--appearance-review")) Title="拾简 · 界面材质验证（数据隔离）";
        var catalogue=Path.Combine(AppContext.BaseDirectory,"Assets","TemplateCatalog.json");
        if(File.Exists(catalogue)) templates=JsonSerializer.Deserialize<List<Template>>(File.ReadAllText(catalogue),BackendClient.Json)??[];
        if(templates.Count>0) ShowTemplates();
        Content=shell;
        Activated+=(_,_)=> { if(haruStyle) NativeWindowGlass.Enable(WinRT.Interop.WindowNative.GetWindowHandle(this),darkStyle); };
        shell.Loaded+=async (_,_)=>await Run(Initialize);
        editTimer.Tick+=async (_,_)=> { editTimer.Stop(); await Run(SaveCurrent); };
        previewTimer.Tick+=async (_,_)=> { previewTimer.Stop(); if(currentPage=="editor") await Run(RenderPreview); };
        reminderTimer.Tick+=async (_,_)=>await Run(CheckApplicationReminders);
        systemSettings.ColorValuesChanged+=(_,_)=>DispatcherQueue.TryEnqueue(()=> { if(preferences.InterfaceStyle=="haru"&&preferences.Theme=="system") _=Run(ApplyPreferences); });
        AppWindow.Closing+=(sender,args)=>
        {
            if(closing) return;
            args.Cancel=true;
            _=Run(async ()=> { editTimer.Stop(); previewTimer.Stop(); reminderTimer.Stop(); CancelJobSearch(); await SaveCurrent(); closing=true; preview?.Close(); api.Dispose(); Close(); });
        };
    }
    private static SolidColorBrush Brush(string hex)
    {
        if(darkStyle) hex=hex switch { "#167D78"=>"#8BD7C1","#A56A05"=>"#F0CB81","#77849A"=>"#ADB6CC",_=>hex };
        string rgb=hex.TrimStart('#'); byte alpha=rgb.Length==8?Convert.ToByte(rgb[..2],16):(byte)255; if(rgb.Length==8) rgb=rgb[2..];
        return new SolidColorBrush(Windows.UI.Color.FromArgb(alpha,Convert.ToByte(rgb[..2],16),Convert.ToByte(rgb.Substring(2,2),16),Convert.ToByte(rgb.Substring(4,2),16)));
    }
    private static TextBlock Text(string text,double size=14,bool bold=false) => new() { Text=text,FontSize=size,FontFamily=new FontFamily(haruStyle?"MiSans":"Segoe UI"),FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal,Foreground=Brush(Ink),TextWrapping=TextWrapping.Wrap };
    private static StackPanel Stack(double spacing=12) => new() { Spacing=spacing };
    private static StackPanel Row(double spacing=10) => new() { Orientation=Orientation.Horizontal,Spacing=spacing };
    private static Border Card(UIElement child,Thickness? padding=null) => new() { Child=child,Padding=padding??new Thickness(22),Background=PanelMaterial,BorderBrush=EdgeMaterial,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(haruStyle?14:12) };
    private Button Button(string label,Func<Task> action,bool primary=false)
    {
        var b=new Button { Content=label,Padding=new Thickness(15,9,15,9),CornerRadius=new CornerRadius(haruStyle?12:7),FontFamily=new FontFamily(haruStyle?"MiSans":"Segoe UI") };
        if(primary) { b.Background=Brush(Accent); b.Foreground=Brush("#FFFFFF"); }
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
        Mark("shellLoaded");
        if(templates.Count>0) Mark("firstPageReady");
        var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var scale=shell.XamlRoot.RasterizationScale;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min((int)(1440*scale),area.Width-80),Math.Min((int)(920*scale),area.Height-60)));
        await api.StartAsync(); Mark("backendReady"); ready=true; saveState.Text="本地服务已连接";
        reminderTimer.Start(); await CheckApplicationReminders();
        if(templates.Count==0) { templates=await api.Get<List<Template>>("templates"); await Navigate("templates"); Mark("firstPageReady"); }
        if(Environment.GetCommandLineArgs().Contains("--benchmark")) { await Benchmark(); return; }
        if(Environment.GetCommandLineArgs().Contains("--iteration-test")) { await IterationOnlyTest(); return; }
        if(Environment.GetCommandLineArgs().Contains("--feature-test")) { await FeatureTest(); return; }
        if(Environment.GetCommandLineArgs().Contains("--self-test"))
        {
            Title="拾简 · 自动验证窗口"; shell.IsHitTestVisible=false;
            try { await SelfTest(); }
            finally { shell.IsHitTestVisible=true; }
        }
    }
    private void BuildSidebar()
    {
        var side=new Grid { Background=Brush("#14233F"),Padding=new Thickness(17,28,17,20) };
        side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); side.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) }); side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var brand=Stack(5); var name=Text("拾简",30,true); name.Foreground=Brush("#FFFFFF"); var mark=Row(10); mark.Children.Add(BrandLogo(46)); mark.Children.Add(name); brand.Children.Add(mark); var sub=Text("RESUME STUDIO",10); sub.CharacterSpacing=150; sub.Foreground=Brush("#8CA6D2"); brand.Children.Add(sub); side.Children.Add(brand);
        var menu=Stack(7); menu.Margin=new Thickness(0,35,0,0);
        foreach(var item in Pages)
        {
            var key=item.Key;
            var b=Button(item.Label,()=>Navigate(key)); b.Tag=key; b.HorizontalAlignment=HorizontalAlignment.Stretch; b.HorizontalContentAlignment=HorizontalAlignment.Left; b.Foreground=Brush("#CBD7EB"); b.Background=new SolidColorBrush(Colors.Transparent); b.BorderThickness=new Thickness(0); b.Padding=new Thickness(14,13,14,13); navigation.Add(b); menu.Children.Add(b);
        }
        Grid.SetRow(menu,1); side.Children.Add(menu);
        var footer=Stack(8); var tagline=Text("把经历写清楚\n让机会更靠近",13); tagline.Foreground=Brush("#A9BEDC"); footer.Children.Add(tagline); var v=Text($"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}  ·  Windows 桌面版",10); v.Foreground=Brush("#7189AE"); footer.Children.Add(v); Grid.SetRow(footer,2); side.Children.Add(footer); shell.Children.Add(side);
    }
    private async Task Navigate(string page)
    {
        int version=++navigationRevision;
        editTimer.Stop(); previewTimer.Stop(); CancelJobSearch(); await SaveCurrent();
        if(version!=navigationRevision) return;
        currentPage=page; notice.IsOpen=false;
        DetachPreview();
        body.Children.Clear(); body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
        UpdateNavigation(page);
        switch(page)
        {
            case "templates": ShowTemplates(); break;
            case "resumes": await ShowResumes(); break;
            case "jobs": await ShowJobs(); break;
            case "sources": await ShowCompanySources(); break;
            case "favorites": await ShowFavorites(); break;
            case "applications": await ShowApplications(); break;
            case "settings": ShowSettings(); break;
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
        if(currentPage!="resumes") return;
        if(list.Count==0) panel.Children.Add(Card(Text("还没有简历。从模板中心选择一套模板，开始填写你的经历。",16)));
        foreach(var r in list)
        {
            var c=Stack(12); c.Children.Add(Text(r.Title,20,true)); c.Children.Add(Text($"{r.Profile.Name}  ·  {r.Profile.Role}  ·  {templates.First(t=>t.Id==r.TemplateId).Name}",13)); c.Children.Add(Text("最近保存："+LocalDate(r.UpdatedAt),11));
            var actions=Row(); actions.Children.Add(Button("继续编辑",async ()=> { current=r; dirty=false; await OpenEditor(); },true)); actions.Children.Add(Button("复制",async ()=> { var clone=JsonSerializer.Deserialize<Resume>(JsonSerializer.Serialize(r,BackendClient.Json),BackendClient.Json)!; clone.Id=Guid.NewGuid().ToString(); clone.Title+=" · 副本"; await api.Save("resumes",clone); await Navigate("resumes"); })); actions.Children.Add(Button("删除",async ()=> { if(await Confirm("删除这份简历？",r.Title+"，删除后无法在应用内恢复。")) { await api.Delete("resumes/"+r.Id); if(current?.Id==r.Id) current=null; await Navigate("resumes"); } })); c.Children.Add(actions); panel.Children.Add(Card(c));
        }
        FillScroll(panel);
    }
    private TextBox Field(string label,string value,Action<string> setter,bool multiline=false,string placeholder="")
    {
        // AcceptsReturn must be set before assigning multi-line content. Initial field loading is not an edit.
        var box=new TextBox { Header=label,PlaceholderText=placeholder,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?112:35,MaxLength=multiline?30000:300 };
        string lastValue=NormalizeLines(value); box.Text=value;
        box.TextChanged+=(_,_)=> { string text=NormalizeLines(box.Text); if(text==lastValue) return; lastValue=text; setter(text); Changed(); }; return box;
    }
    private void Changed() { CaptureUndo(); dirty=true; revision++; saveState.Text="有未保存的修改…"; editTimer.Stop(); editTimer.Start(); previewTimer.Stop(); if(preferences.LivePreview) previewTimer.Start(); }
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
    private async Task ExportPdf()
    {
        await CheckPdf(true);
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
        var sourceFile=await Windows.Storage.StorageFile.GetFileFromPathAsync(file.Path);
        using var input=await sourceFile.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder=await BitmapDecoder.CreateAsync(input);
        double scale=Math.Min(1d,Math.Min(480d/decoder.OrientedPixelWidth,640d/decoder.OrientedPixelHeight));
        uint width=(uint)Math.Max(1,Math.Round(decoder.OrientedPixelWidth*scale)),height=(uint)Math.Max(1,Math.Round(decoder.OrientedPixelHeight*scale));
        var transform=new BitmapTransform { ScaledWidth=width,ScaledHeight=height,InterpolationMode=BitmapInterpolationMode.Fant };
        var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,transform,ExifOrientationMode.RespectExifOrientation,ColorManagementMode.DoNotColorManage);
        using var output=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,width,height,96,96,pixels.DetachPixelData());
        await encoder.FlushAsync(); output.Seek(0);
        using var reader=new DataReader(output.GetInputStreamAt(0)); await reader.LoadAsync((uint)output.Size); var bytes=new byte[(int)output.Size]; reader.ReadBytes(bytes);
        current.Photo="data:image/jpeg;base64,"+Convert.ToBase64String(bytes); Changed();
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
        c.Children.Add(JobLink(j));
        var actions=Row(); actions.Children.Add(Button("详情",async ()=> { var content=Stack(); content.Children.Add(Text(j.Company+" · "+j.Location,14,true)); content.Children.Add(Text(j.Description,14)); var dialog=new ContentDialog { XamlRoot=shell.XamlRoot,Title=j.Title,Content=new ScrollViewer { Content=content,MaxHeight=480 },PrimaryButtonText="前往投递",CloseButtonText="返回" }; if(await dialog.ShowAsync()==ContentDialogResult.Primary) await OpenJob(j); })); actions.Children.Add(Button("前往投递 ↗",()=>OpenJob(j),true));
        actions.Children.Add(Button(favorite?"取消收藏":"收藏职位",async ()=> { if(favorite) { await api.Delete("favorites/"+j.Id); await Navigate("favorites"); } else { await api.Save("favorites",j); Success("已收藏",j.Title); } })); if(favorite) { actions.Children.Add(Button("编辑收藏",()=>EditFavorite(j,false))); actions.Children.Add(Button("自动补全",()=>ResolveFavorite(j))); } actions.Children.Add(Button("复制链接",()=>CopyJobLink(j))); c.Children.Add(actions); c.Children.Add(Button("针对岗位制作简历",()=>FitJob(j))); return Card(c);
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
    private void ShowAbout()
    {
        Heading("使用说明","简历制作与求职跟进，在一个桌面工作区完成。"); var content=Stack(22);
        foreach(var item in new[] {("01  GitHub 高星模板","8 套岗位适配，覆盖前端、后端、嵌入式与通用型，来自 Awesome-CV、billryan/resume、Deedy-Resume 和 Jake’s Resume。卡片显示仓库来源、核实星数和许可。版式从原仓库 LaTeX 模板移植为中文 A4，原文件与许可随应用保存。6 套旧模板保留以兼容已有简历。"),("02  制作与辅助","填写信息与经历 → 调整模块顺序和样式 → 检查模块状态与规则建议 → 查看 A4 内容余量 → 导出 PDF。支持 Markdown 表格与标题日期。支持格式按钮、撤销重做和最近 20 个历史版本；PDF 导出前检查真实分页。岗位对照列出要求与简历依据，可按设置的兼容接口获取 AI 建议，逐条确认后生成独立岗位简历。AI 提示词按钮仍只复制文本。"),("03  招聘与跨平台收藏","21个公司来源可在发现职位汇总查询，支持腾讯及 Greenhouse、Lever、Ashby、SmartRecruiters、Workday；可添加国内外公司官网并查看真实接入状态。BOSS、猎聘、智联、前程无忧、拉勾、牛客和企业官网职位可粘贴详情链接，尝试自动解析公开信息后收藏。也可正常浏览详情并读取可见职位内容。原始链接保留访问参数，规范地址单独去重，可按平台筛选。平台账号收藏不会自动同步。"),("04  直接采集的范围","当前自动获取的是公开职位源。BOSS 搜索页依赖脚本和访问状态，robots 限制搜索参数路径，当前未实现其匿名批量采集；单个公开详情可尝试直接解析，受限页面可在“浏览并读取”中正常完成验证后读取可见内容。未实现验证码绕过、外部浏览器 Cookie 读取或自动投递。"),("05  投递与数据","“前往投递”打开原页面并记录“已打开”；实际投递在原站完成，再手动更新进度。简历、收藏、投递记录由本地 Java 服务保存。记录进度时间线、笔试／面试日期和复盘，可确认所用简历并固定投递快照。应用运行时提前 24 小时及到期提醒，关闭期间不通知，重新打开补查待办。上一版备份与最多 20 个历史版本压缩保存。"),("06  技术与来源","客户端为原生 WinUI 3 / .NET 8，后端 Java 21，WebView2 用于预览和 PDF。开源模板与 resume-helper 功能来源说明见安装目录 THIRD_PARTY_NOTICES.md。")}) { var s=Stack(); s.Children.Add(Text(item.Item1,20,true)); s.Children.Add(Text(item.Item2,15)); content.Children.Add(Card(s)); }
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
            await VerifyIteration(output);
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new { status="passed",savedResumeId=r.Id,jobCount=jobs.Items.Count,jobSource="腾讯招聘",sampleJobUrl=job.Url,templateCount=templates.Count,githubTemplateCount=templates.Count(t=>t.Origin=="github"),companySourceCount=shownCompanyJobs.Sources.Count,aggregateJobCount=shownCompanyJobs.Items.Count,companies=shownCompanyJobs.Items.Select(x=>x.Company).Distinct().ToList(),pdfBytes=new FileInfo(Path.Combine(output,"resume-preview.pdf")).Length,checks=new[] {"WinUI launch","Java startup","native TextBox editing and auto-save","resume create/update/read","WebView preview","PDF export","all 14 template PDFs","multipage PDF","HTML escaping","live recruitment","favorites persistence","application status update","module statuses and suggestions","native shared-link parsing","BOSS link local collection","tracking URL deduplication","Markdown tables","company registry native UI","multi-employer aggregation native UI"} },BackendClient.Json));
            saveState.Text="桌面链路验证通过";
        }
        catch(Exception ex) { await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {status="failed",error=ex.ToString()},BackendClient.Json)); try { await SaveNativePreview(Path.Combine(output,"failed-state.png")); } catch(Exception) { } throw; }
    }
}
