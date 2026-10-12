using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,BitmapImage> thumbnails=[];
    private string templateCategory="全部",templateOrigin="github";
    private void ShowTemplates()
    {
        Heading("模板中心","GitHub 高星简历模板，按前端、后端、嵌入式和通用型分类。");
        var panel=Stack(18);
        var hero=Stack(7); hero.Children.Add(Text("从开源好模板，开始你的简历。",24,true));
        hero.Children.Add(Text($"{templates.Count(t=>t.Origin=="github")} 套岗位适配  ·  4 个高星模板仓库  ·  实时预览与 PDF 导出",14));
        hero.Children.Add(Text("保留原模板版式特征，移植为中文 A4 编辑版；岗位分类与填写引导由拾简补充。星数核实于 2026-10-12。",12)); panel.Children.Add(Card(hero,new Thickness(24)));
        var filters=Row(); var search=new TextBox { PlaceholderText="模板、仓库或技术关键词",Width=270 };
        var category=new ComboBox { Width=150,ItemsSource=new[] {"全部","前端开发","后端开发","嵌入式开发","通用型"},SelectedItem=templateCategory };
        var origin=new ComboBox { Width=160,ItemsSource=new[] {"GitHub 高星模板","内置旧模板","全部来源"},SelectedIndex=templateOrigin=="github"?0:templateOrigin=="builtin"?1:2 };
        var sort=new ComboBox { Width=135,ItemsSource=new[] {"高星优先","名称排序"},SelectedIndex=0 };
        filters.Children.Add(search); filters.Children.Add(category); filters.Children.Add(origin); filters.Children.Add(sort); panel.Children.Add(filters);
        var count=Text("",12); panel.Children.Add(count);
        var cards=new Grid { ColumnSpacing=18,RowSpacing=18 }; int columns=3;
        void Refresh()
        {
            templateCategory=(string?)category.SelectedItem??"全部"; templateOrigin=origin.SelectedIndex==0?"github":origin.SelectedIndex==1?"builtin":"all";
            cards.Children.Clear(); cards.RowDefinitions.Clear(); cards.ColumnDefinitions.Clear();
            for(int i=0;i<columns;i++) cards.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            var found=templates.Where(t=>(t.Name+" "+t.Tags+" "+t.SourceRepo).Contains(search.Text,StringComparison.OrdinalIgnoreCase)&&(templateCategory=="全部"||t.Category==templateCategory)&&(templateOrigin=="all"||t.Origin==templateOrigin));
            var list=(sort.SelectedIndex==0?found.OrderByDescending(t=>t.Stars).ThenBy(t=>t.Category):found.OrderBy(t=>t.Name)).ToList();
            count.Text=$"当前 {list.Count} 套  ·  {templateCategory}  ·  {(templateOrigin=="github"?"GitHub 高星模板":templateOrigin=="builtin"?"内置旧模板":"全部来源")}";
            for(int i=0;i<(list.Count+columns-1)/columns;i++) cards.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            for(int i=0;i<list.Count;i++)
            {
                var t=list[i]; var c=Stack(11); c.Children.Add(Miniature(t)); c.Children.Add(Text(t.Name,17,true)); c.Children.Add(Text(t.Category+"  ·  "+(t.Origin=="github"?$"★ {t.Stars:N0}":"内置"),12,true)); c.Children.Add(Text(t.Description,12));
                if(t.Origin=="github")
                {
                    c.Children.Add(Text(t.SourceRepo+"  ·  "+t.License,11));
                    var source=Button("来源与移植说明 ↗",()=>ShowTemplateSource(t)); source.FontSize=11; source.HorizontalAlignment=HorizontalAlignment.Left; c.Children.Add(source);
                }
                var buttons=Row(7); buttons.Children.Add(Button("预览",()=>PreviewTemplate(t))); buttons.Children.Add(Button("使用模板",()=>CreateResume(t),true)); c.Children.Add(buttons);
                var card=Card(c,new Thickness(18)); Grid.SetRow(card,i/columns); Grid.SetColumn(card,i%columns); cards.Children.Add(card);
            }
            if(list.Count==0) { cards.RowDefinitions.Add(new RowDefinition()); cards.Children.Add(Text("没有匹配的模板，试试其他分类或关键词。")); }
        }
        cards.SizeChanged+=(_,e)=> { int next=e.NewSize.Width<630?1:e.NewSize.Width<970?2:3; if(next!=columns) { columns=next; Refresh(); } };
        search.TextChanged+=(_,_)=>Refresh(); category.SelectionChanged+=(_,_)=>Refresh(); origin.SelectionChanged+=(_,_)=>Refresh(); sort.SelectionChanged+=(_,_)=>Refresh(); Refresh(); panel.Children.Add(cards); FillScroll(panel);
    }
    private async Task ShowTemplateSource(Template t)
    {
        var content=Stack(14);
        foreach(string text in new[] {t.SourceRepo,$"GitHub 星数：{t.Stars:N0}（{t.StarsCheckedAt} 核实）",$"许可：{t.License}",$"原模板文件：{t.SourceFile}",$"对应提交：{t.SourceCommit}",t.Adaptation,"原始模板源码与许可保存在安装目录 ThirdParty；不需要安装 LaTeX 即可使用本应用的移植版。"}) content.Children.Add(Text(text,13));
        var result=await new ContentDialog { XamlRoot=shell.XamlRoot,Title="模板来源",Content=new ScrollViewer { Content=content,MaxHeight=470 },PrimaryButtonText="打开原仓库",CloseButtonText="返回" }.ShowAsync();
        if(result==ContentDialogResult.Primary) await Launch(t.SourceUrl);
    }
    private Border Miniature(Template t)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"Assets","Templates",t.Id+".png");
        if(File.Exists(path))
        {
            if(!thumbnails.TryGetValue(t.Id,out var image)) { image=new BitmapImage { DecodePixelWidth=360,UriSource=new Uri(path) }; thumbnails[t.Id]=image; }
            return new Border { Background=haruStyle?Brush(darkStyle?"#2910101A":"#38FFFFFF"):Brush("#EFF3F9"),Padding=new Thickness(8),CornerRadius=new CornerRadius(haruStyle?12:8),Child=new Image { Source=image,Height=250,Stretch=Microsoft.UI.Xaml.Media.Stretch.Uniform } };
        }
        var page=Stack(8); page.Children.Add(Text("你的姓名",18,true)); page.Children.Add(Text("求职意向 · 联系方式",9));
        foreach(string title in t.Sections.Take(3))
        {
            var label=Text(title,10,true); label.Foreground=Brush(t.Color); page.Children.Add(label);
            page.Children.Add(new Border { Background=Brush("#D9E0EA"),Height=3,Width=155,HorizontalAlignment=HorizontalAlignment.Left }); page.Children.Add(new Border { Background=Brush("#E8EDF3"),Height=3,Width=126,HorizontalAlignment=HorizontalAlignment.Left });
        }
        var paper=Card(page,new Thickness(18)); paper.Width=220; paper.Height=240; return new Border { Background=Brush("#EFF3F9"),Padding=new Thickness(10),CornerRadius=new CornerRadius(8),Child=paper };
    }
    private static Resume Blank(Template t) => new() { Title=t.Name+"简历",TemplateId=t.Id,Color=t.Color,FontSize=t.Origin=="github"?11:12,LineHeight=t.Origin=="github"?1.45:1.6,Sections=t.Sections.Select(s=>new ResumeSection { Title=s }).ToList() };
    private static Resume Demo(Template t)
    {
        var r=Blank(t); r.Title="模板预览示例"; r.Profile=new Profile { Name="示例姓名",Role=t.Category=="通用型"?"软件工程师":t.Category+"工程师",Phone="138 0000 0000",Email="example@email.com",City="期望城市",Summary="内容仅用于展示版式，请按真实经历填写。" };
        foreach(var s in r.Sections)
        {
            if(s.Title.Contains("教育")) s.Content="## 示例大学 · 计算机相关专业 | 2023.09 - 2027.06\n本科\n- 相关课程：数据结构、操作系统、计算机网络。";
            else if(s.Title.Contains("技能")) s.Content=t.Category switch {
                "前端开发"=>"- 语言与框架：[填写 JavaScript / TypeScript、Vue / React 的实际掌握情况]\n- 工程实践：[填写组件、构建工具、兼容性和测试经验]",
                "后端开发"=>"- 语言与服务：[填写 Java / Go / Python、框架与接口开发经验]\n- 存储与可靠性：[填写数据库、缓存、协议和测试经验]",
                "嵌入式开发"=>"- 语言与系统：[填写 C / C++、RTOS / Linux 的实际掌握情况]\n- 平台与调试：[填写 MCU / SoC、接口协议、调试工具]",
                _=>"- 编程语言：[填写实际掌握的技术]\n- 工具与实践：[填写目标岗位相关能力]" };
            else if(s.Title.Contains("作品")) s.Content="- 个人主页：[填写作品链接]\n- 开源贡献：[填写仓库与实际贡献]";
            else s.Content=$"## {s.Title}示例 | 时间范围\n角色 / 技术栈 / 平台\n- 说明你负责的设计、开发或调试工作。\n- 描述可核实的成果与 **验证方式**，不编造指标。";
        }
        return r;
    }
    private async Task PreviewTemplate(Template t)
    {
        var web=new WebView2 { Width=720,Height=560 };
        var dialog=new ContentDialog { XamlRoot=shell.XamlRoot,Title=t.Name,Content=web,PrimaryButtonText="使用模板",CloseButtonText="返回",DefaultButton=ContentDialogButton.Primary };
        web.DefaultBackgroundColor=haruStyle?Microsoft.UI.Colors.Transparent:Microsoft.UI.Colors.White;
        dialog.Opened+=async (_,_)=>await Run(async ()=> { await InitializeWeb(web); web.NavigateToString(PreviewHtml(Demo(t),t)); });
        var result=await dialog.ShowAsync(); web.Close(); if(result==ContentDialogResult.Primary) await CreateResume(t);
    }
}
