using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task SaveNativePreview(string path)
    {
        await Task.Delay(250);
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(shell);
        if(bitmap.PixelWidth==0||bitmap.PixelHeight==0) return;
        var buffer=await bitmap.GetPixelsAsync(); var pixels=new byte[buffer.Length];
        using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file=await folder.CreateFileAsync(Path.GetFileName(path),Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels); await encoder.FlushAsync();
    }
    private Func<int,Task>? companySearch;
    private JobResult? shownCompanyJobs;
    private async Task ShowJobs()
    {
        Heading("发现职位","汇总企业官网公开岗位，可添加公司、筛选来源、收藏与跳转投递。");
        var companies=await api.Get<List<CompanySource>>("job-sources");
        body.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        var controls=Stack(11); var filters=Row();
        var keyword=new TextBox { PlaceholderText="关键词：Java / embedded / 前端",Text=jobKeyword,Width=260 }; var city=new TextBox { PlaceholderText="城市 / 地点",Text=jobCity,Width=155 };
        var choices=new List<CompanySource> { new() { Id="all",Name="全部启用的企业官网" } }; choices.AddRange(companies);
        var provider=new ComboBox { ItemsSource=choices,SelectedItem=choices.FirstOrDefault(x=>x.Id==jobProvider)??choices[0],Width=210 };
        filters.Children.Add(keyword); filters.Children.Add(city); filters.Children.Add(provider);
        var status=Text($"已配置 {companies.Count} 个来源。公共招聘系统可直接返回岗位，动态官网可能需要专用适配。",12); var results=Stack(14); var sourcePanel=Stack(7);
        async Task Search(int page)
        {
            if(searching) return; searching=true;
            try
            {
                jobPage=page; jobKeyword=keyword.Text.Trim(); jobCity=city.Text.Trim(); jobProvider=((CompanySource)provider.SelectedItem).Id; status.Text="正在查询企业官网…（并发采集，本轮最多30秒）";
                var r=await api.Get<JobResult>($"jobs?provider={Uri.EscapeDataString(jobProvider)}&keyword={Uri.EscapeDataString(jobKeyword)}&city={Uri.EscapeDataString(jobCity)}&page={page}");
                if(currentPage!="jobs") return;
                shownCompanyJobs=r;
                status.Text=$"{r.Message}\n第 {page} 页 / 本页 {r.Items.Count} 条 · 来源返回数量 {r.Total}（部分源仅为当前页） · {LocalDate(r.FetchedAt)} "+(r.Cached?"（缓存）":"");
                results.Children.Clear(); foreach(var job in r.Items) results.Children.Add(JobCard(job,false));
                if(r.Items.Count==0) results.Children.Add(Card(Text("当前没有匹配职位。可清空关键词与城市、选择其他公司，或在来源状态中打开官网。",15)));
                sourcePanel.Children.Clear();
                foreach(var s in r.Sources)
                {
                    var line=Stack(4); var caption=Text($"{s.Name} · {(s.Status=="ok"?"成功":s.Status=="unsupported"?"需专用适配":"未获取")} · {s.LastCount} 条",12,true); caption.Foreground=Brush(s.Status=="ok"?"#167D78":"#A56A05"); line.Children.Add(caption); line.Children.Add(Text(s.Message,11)); line.Children.Add(Button("打开招聘官网 ↗",()=>Launch(s.Url))); sourcePanel.Children.Add(Card(line,new Thickness(12)));
                }
            }
            finally { searching=false; }
        }
        companySearch=Search;
        filters.Children.Add(Button("查询官网职位",()=>Search(1),true)); controls.Children.Add(filters);
        var actions=Row(); actions.Children.Add(Button("公司来源管理",()=>Navigate("sources"))); actions.Children.Add(Button("添加公司官网",()=>EditCompanySource(new CompanySource()))); actions.Children.Add(Button("BOSS直聘 ↗",()=>Launch("https://www.zhipin.com/web/geek/job?query="+Uri.EscapeDataString(keyword.Text)))); actions.Children.Add(Button("牛客校招 ↗",()=>Launch("https://www.nowcoder.com/jobs"))); controls.Children.Add(actions); controls.Children.Add(status); controls.Margin=new Thickness(0,0,0,15); body.Children.Add(controls);
        var list=Stack(15); list.Children.Add(new Expander { Header="本次各公司来源状态",Content=sourcePanel,HorizontalAlignment=HorizontalAlignment.Stretch }); list.Children.Add(results); var paging=Row(); paging.Children.Add(Button("上一页",()=>Search(Math.Max(1,jobPage-1)))); paging.Children.Add(Button("下一页",()=>Search(Math.Min(100,jobPage+1)))); list.Children.Add(paging);
        var scroll=new ScrollViewer { Content=list,HorizontalScrollMode=ScrollMode.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch }; scroll.SizeChanged+=(_,e)=>list.Width=Math.Max(0,e.NewSize.Width-16); Grid.SetRow(scroll,1); body.Children.Add(scroll);
        keyword.KeyDown+=async (_,e)=> { if(e.Key==Windows.System.VirtualKey.Enter) await Run(()=>Search(1)); };
    }
    private async Task ShowCompanySources()
    {
        Heading("公司招聘官网","添加公司官网与公开招聘系统，查看实际采集状态。");
        var panel=Stack(17); panel.Children.Add(Button("＋ 添加公司官网",()=>EditCompanySource(new CompanySource()),true));
        panel.Children.Add(Card(Text("支持腾讯、Greenhouse、Lever、Ashby、SmartRecruiters、Workday，以及网页公开 JobPosting 信息。使用同类招聘系统的公司可直接配置公司标识或职位板链接。普通官网会尝试提取公开岗位，动态页面未适配时会明确显示，不把招聘首页当作职位。",14)));
        var list=await api.Get<List<CompanySource>>("job-sources"); var filter=Row(); var search=new TextBox { PlaceholderText="公司名称 / 招聘系统",Width=280 }; var region=new ComboBox { ItemsSource=new[] {"全部","国内","全球"},SelectedIndex=0,Width=130 }; filter.Children.Add(search); filter.Children.Add(region); panel.Children.Add(filter);
        var cards=Stack(14); void Refresh()
        {
            cards.Children.Clear(); foreach(var s in list.Where(s=>(s.Name+" "+s.Adapter).Contains(search.Text,StringComparison.OrdinalIgnoreCase)&&((string)region.SelectedItem=="全部"||s.Region==(string)region.SelectedItem)))
            {
                var c=Stack(9); c.Children.Add(Text(s.Name+"  ·  "+s.Region,19,true)); c.Children.Add(Text(s.Adapter+"  ·  "+s.Status+(s.LastQueryAt==""?"":"  ·  "+LocalDate(s.LastQueryAt)),12)); c.Children.Add(Text(s.Message,12)); c.Children.Add(Text(s.Url,11));
                var row=Row(); var enable=new ToggleSwitch { Header="汇总时启用",IsOn=s.Enabled }; enable.Toggled+=async (_,_)=>await Run(async ()=> { s.Enabled=enable.IsOn; await api.Save("sources",s); }); row.Children.Add(enable); row.Children.Add(Button("查询该公司",async ()=> { jobProvider=s.Id; await Navigate("jobs"); })); row.Children.Add(Button("官网 ↗",()=>Launch(s.Url))); row.Children.Add(Button("编辑",()=>EditCompanySource(s))); if(!s.Builtin) row.Children.Add(Button("删除",async ()=> { if(await Confirm("删除公司来源？",s.Name)) { await api.Delete("sources/"+s.Id); await Navigate("sources"); } })); c.Children.Add(row); cards.Children.Add(Card(c));
            }
        }
        search.TextChanged+=(_,_)=>Refresh(); region.SelectionChanged+=(_,_)=>Refresh(); Refresh(); panel.Children.Add(cards); FillScroll(panel);
    }
    private async Task EditCompanySource(CompanySource source)
    {
        var content=Stack(12); var name=new TextBox { Header="公司名称",Text=source.Name }; var url=new TextBox { Header="招聘官网链接（HTTPS）",Text=source.Url };
        var types=new[] {"jsonld","greenhouse","lever","ashby","smartrecruiters","workday","tencent","arbeitnow"}; var adapter=new ComboBox { Header="接入方式",ItemsSource=types,SelectedItem=source.Adapter,HorizontalAlignment=HorizontalAlignment.Stretch }; var slug=new TextBox { Header="ATS 公司 / Board 标识（通用官网、Workday 无需填写）",Text=source.Slug,PlaceholderText="例如 stripe、palantir、notion、BoschGroup" }; var region=new ComboBox { Header="范围",ItemsSource=new[] {"国内","全球"},SelectedItem=source.Region };
        foreach(var control in new UIElement[] {name,url,adapter,slug,region}) content.Children.Add(control);
        content.Children.Add(Text("Greenhouse、Lever、Ashby、SmartRecruiters 需从企业公开招聘页找到 Board / 公司标识。Workday 填写主机与职位板路径，无需标识。jsonld 只读取公开 JobPosting，动态官网可能需要专用适配。",12));
        url.LostFocus+=(_,_)=>
        {
            if(!Uri.TryCreate(url.Text,UriKind.Absolute,out var u)) return;
            var segments=u.AbsolutePath.Trim('/').Split('/'); if(segments.Length==0) return;
            if(u.Host=="jobs.lever.co") { adapter.SelectedItem="lever"; slug.Text=segments[0]; }
            if(u.Host=="jobs.ashbyhq.com") { adapter.SelectedItem="ashby"; slug.Text=segments[0]; }
            if(u.Host is "boards.greenhouse.io" or "job-boards.greenhouse.io") { adapter.SelectedItem="greenhouse"; slug.Text=segments[0]; }
            if(u.Host=="jobs.smartrecruiters.com") { adapter.SelectedItem="smartrecruiters"; slug.Text=segments[0]; }
            if(u.Host.EndsWith(".myworkdayjobs.com")) { adapter.SelectedItem="workday"; if(segments.Length>1&&segments[0].Contains('-')) url.Text=u.GetLeftPart(UriPartial.Authority)+"/"+segments[1]; }
        };
        if(await new ContentDialog { XamlRoot=shell.XamlRoot,Title="配置公司招聘官网",Content=content,PrimaryButtonText="保存来源",CloseButtonText="取消" }.ShowAsync()!=ContentDialogResult.Primary) return;
        source.Name=name.Text.Trim(); source.Url=url.Text.Trim(); source.Adapter=(string)adapter.SelectedItem; source.Slug=slug.Text.Trim(); source.Region=(string)region.SelectedItem; await api.Save("sources",source); await Navigate("sources");
    }
}
