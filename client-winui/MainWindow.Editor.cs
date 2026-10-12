using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResumeStudio.Services;
using Windows.System;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private int selectedEditorPart;
    private string editorResumeId="";
    private ListBox? editorParts;
    private Grid? editorFields;

    private AppBarButton EditorAction(string label,Symbol icon,Func<Task> action)
    {
        var button=new AppBarButton { Label=label,Icon=new SymbolIcon(icon) };
        button.Click+=async (_,_)=>await Run(action); return button;
    }
    private async Task OpenEditor()
    {
        if(current is null) return;
        if(editorResumeId!=current.Id) { selectedEditorPart=0; editorResumeId=current.Id; }
        currentPage="editor"; DetachPreview(); body.Children.Clear(); body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
        Heading("简历工作台","选择模块编辑，右侧保留预览；自动保存与预览独立运行。"); UpdateNavigation("editor");
        body.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(480) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(16) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var toolbar=new CommandBar { DefaultLabelPosition=CommandBarDefaultLabelPosition.Right,IsOpen=false,Margin=new Thickness(0,0,0,12),Background=Brush(Surface) };
        toolbar.PrimaryCommands.Add(EditorAction("我的简历",Symbol.Back,()=>Navigate("resumes")));
        toolbar.PrimaryCommands.Add(EditorAction("保存",Symbol.Save,SaveCurrent));
        toolbar.PrimaryCommands.Add(EditorAction("导出 PDF",Symbol.Download,ExportPdf));
        toolbar.PrimaryCommands.Add(EditorAction("刷新预览",Symbol.Refresh,RenderPreview));
        toolbar.SecondaryCommands.Add(EditorAction("备份 JSON",Symbol.SaveLocal,ExportJson));
        toolbar.SecondaryCommands.Add(EditorAction("查看打印版",Symbol.Print,async ()=> { await WaitForPreview(); preview!.CoreWebView2.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser); }));
        toolbar.SecondaryCommands.Add(EditorAction("检查与建议",Symbol.Help,ShowResumeCheck));
        toolbar.SecondaryCommands.Add(EditorAction("AI 提示词",Symbol.Copy,CopyAiPrompt));
        var saveKey=new KeyboardAccelerator { Key=VirtualKey.S,Modifiers=VirtualKeyModifiers.Control };
        saveKey.Invoked+=async (_,e)=> { e.Handled=true; await Run(SaveCurrent); }; toolbar.KeyboardAccelerators.Add(saveKey);
        Grid.SetColumnSpan(toolbar,3); body.Children.Add(toolbar);

        var form=new Grid { ColumnSpacing=12 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(136) }); form.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var moduleColumn=Stack(10); moduleColumn.Children.Add(Text("内容目录",14,true));
        editorParts=new ListBox { HorizontalAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(0),Background=Brush(Surface) };
        var labels=new List<string> { "基本信息","排版设置" }; labels.AddRange(current.Sections.Select(s=>s.Title+(s.Visible?"":" · 隐藏")));
        editorParts.ItemsSource=labels;
        editorParts.SelectionChanged+=(_,_)=> { if(editorParts.SelectedIndex>=0) { selectedEditorPart=editorParts.SelectedIndex; ShowEditorPart(); } }; moduleColumn.Children.Add(editorParts);
        moduleColumn.Children.Add(Button("＋ 添加模块",async ()=> { current.Sections.Add(new ResumeSection { Title="自定义模块" }); Changed(); selectedEditorPart=current.Sections.Count+1; await SaveCurrent(); await OpenEditor(); }));
        var modulesScroll=new ScrollViewer { Content=moduleColumn,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        form.Children.Add(modulesScroll); editorFields=new Grid(); Grid.SetColumn(editorFields,1); form.Children.Add(editorFields);
        Grid.SetRow(form,1); body.Children.Add(form);
        editorParts.SelectedIndex=Math.Clamp(selectedEditorPart,0,labels.Count-1);

        preview??=new WebView2 { HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch };
        var previewPanel=new Grid(); previewPanel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); previewPanel.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        capacityText=Text("正在估算 A4 内容容量…",11); capacityText.Margin=new Thickness(12,9,12,9); previewPanel.Children.Add(capacityText); Grid.SetRow(preview,1); previewPanel.Children.Add(preview);
        previewHost=previewPanel;
        var frame=Card(previewPanel,new Thickness(0)); Grid.SetColumn(frame,2); Grid.SetRow(frame,1); body.Children.Add(frame);
        await InitializeWeb(preview); await RenderPreview(); await UpdateCapacity(preview);
    }
    private void ShowEditorPart()
    {
        if(current is null||editorFields is null) return;
        var resume=current; var fields=Stack(13); editorFields.Children.Clear();
        if(selectedEditorPart==0)
        {
            fields.Children.Add(Text("基本信息",20,true));
            fields.Children.Add(Field("简历名称",resume.Title,v=>resume.Title=v));
            fields.Children.Add(Field("姓名",resume.Profile.Name,v=>resume.Profile.Name=v)); fields.Children.Add(Field("求职意向",resume.Profile.Role,v=>resume.Profile.Role=v));
            fields.Children.Add(Field("电话",resume.Profile.Phone,v=>resume.Profile.Phone=v)); fields.Children.Add(Field("邮箱",resume.Profile.Email,v=>resume.Profile.Email=v));
            fields.Children.Add(Field("城市",resume.Profile.City,v=>resume.Profile.City=v)); fields.Children.Add(Field("个人主页 / 作品链接",resume.Profile.Link,v=>resume.Profile.Link=v));
            fields.Children.Add(Field("个人简介",resume.Profile.Summary,v=>resume.Profile.Summary=v,true));
            fields.Children.Add(Button("添加证件照",ImportPhoto)); fields.Children.Add(Button("移除照片",()=> { resume.Photo=""; Changed(); return Task.CompletedTask; }));
        }
        else if(selectedEditorPart==1)
        {
            fields.Children.Add(Text("排版设置",20,true));
            var choices=new ComboBox { Header="模板",ItemsSource=templates,SelectedItem=templates.First(t=>t.Id==resume.TemplateId),HorizontalAlignment=HorizontalAlignment.Stretch };
            choices.SelectionChanged+=(_,_)=> { if(choices.SelectedItem is Template t) { resume.TemplateId=t.Id; resume.Color=t.Color; Changed(); } }; fields.Children.Add(choices);
            var colors=Row(5); foreach(string color in new[] {"#245AE8","#167D78","#353D59","#222222","#7955B4","#1E668A"}) { var button=Button("●",()=> { resume.Color=color; Changed(); return Task.CompletedTask; }); button.Foreground=Brush(color); button.Padding=new Thickness(6,4,6,4); colors.Children.Add(button); } fields.Children.Add(colors);
            var size=new Slider { Header="正文字号",Minimum=10,Maximum=16,StepFrequency=0.5,Value=resume.FontSize }; size.ValueChanged+=(_,_)=> { resume.FontSize=size.Value; Changed(); }; fields.Children.Add(size);
            var spacing=new Slider { Header="行间距",Minimum=1.2,Maximum=2,StepFrequency=0.1,Value=resume.LineHeight }; spacing.ValueChanged+=(_,_)=> { resume.LineHeight=spacing.Value; Changed(); }; fields.Children.Add(spacing);
            fields.Children.Add(Text("界面主题不会改变简历的打印配色；PDF 保持白色纸张。",12));
        }
        else if(selectedEditorPart-2<resume.Sections.Count)
        {
            var section=resume.Sections[selectedEditorPart-2]; fields.Children.Add(Text(section.Title,20,true));
            fields.Children.Add(Field("模块标题",section.Title,v=>section.Title=v));
            var input=Field("内容",section.Content,v=>section.Content=v,true,"## 项目 / 经历名称 | 时间\n- 技术、职责与真实成果"); input.MinHeight=340; fields.Children.Add(input);
            fields.Children.Add(Text("支持小标题、时间、列表、加粗和 Markdown 表格。只编辑当前模块，预览始终展示整份简历。",12));
            var visible=new ToggleSwitch { Header="显示模块",IsOn=section.Visible }; visible.Toggled+=(_,_)=> { section.Visible=visible.IsOn; Changed(); }; fields.Children.Add(visible);
            fields.Children.Add(Button("上移模块",()=>MoveEditorSection(section,-1))); fields.Children.Add(Button("下移模块",()=>MoveEditorSection(section,1)));
            fields.Children.Add(Button("移除模块",async ()=> { if(await Confirm("移除模块？",section.Title)) { resume.Sections.Remove(section); selectedEditorPart=Math.Max(0,selectedEditorPart-1); Changed(); await SaveCurrent(); await OpenEditor(); } }));
        }
        var scroll=new ScrollViewer { Content=Card(fields,new Thickness(18)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch };
        scroll.SizeChanged+=(_,e)=>fields.Width=Math.Max(0,e.NewSize.Width-52); editorFields.Children.Add(scroll);
    }
    private async Task MoveEditorSection(ResumeSection section,int offset)
    {
        if(current is null) return; int index=current.Sections.IndexOf(section),target=index+offset;
        if(target<0||target>=current.Sections.Count) return;
        current.Sections.RemoveAt(index); current.Sections.Insert(target,section); selectedEditorPart=target+2;
        Changed(); await SaveCurrent(); await OpenEditor();
    }
}
