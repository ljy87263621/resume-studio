using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private TextBlock? capacityText;
    private async Task ShowResumeCheck()
    {
        if(current is null) return;
        var result=await api.Post<ResumeCheck>("resume-check",current);
        var content=Stack(14);
        foreach(var module in result.Modules)
        {
            var block=Stack(6); var title=Text(module.Title+"  ·  "+module.Status,15,true);
            title.Foreground=Brush(module.Status=="已填"?"#167D78":module.Status=="待优化"?"#A56A05":"#77849A"); block.Children.Add(title);
            foreach(var suggestion in module.Suggestions) block.Children.Add(Text("• "+suggestion,13)); content.Children.Add(Card(block,new Thickness(14)));
        }
        foreach(string tip in result.Tips) content.Children.Add(Text(tip,12));
        var t=templates.First(x=>x.Id==current.TemplateId);
        content.Children.Add(Text(t.Category switch {
            "嵌入式开发"=>"填写重点：硬件平台 → 系统环境 → 外设 / 协议 → 个人实现 → 调试与验证。",
            "前端开发"=>"填写重点：框架与工程工具 → 组件 / 页面职责 → 数据交互 → 兼容性与性能验证。",
            "后端开发"=>"填写重点：技术栈 → 接口与数据模型 → 个人职责 → 性能 / 稳定性验证。",
            _=>"填写重点：真实背景 → 岗位相关技能 → 个人贡献 → 可核实成果。" },14,true));
        await new ContentDialog { XamlRoot=shell.XamlRoot,Title="模块状态与内容建议",Content=new ScrollViewer { Content=content,MaxHeight=560 },CloseButtonText="返回编辑" }.ShowAsync();
    }
    private async Task CopyAiPrompt()
    {
        if(current is null) return;
        var result=await new ContentDialog { XamlRoot=shell.XamlRoot,Title="AI 写简历提示词",Content="复制生成规则和当前简历 JSON，交给你选择的 AI 工具。要求保留真实事实、标明待确认项，并输出本应用可导入的 JSON。此按钮只复制文本，不连接在线 AI。",PrimaryButtonText="复制提示词",CloseButtonText="返回" }.ShowAsync();
        if(result!=ContentDialogResult.Primary) return;
        var text="请依据以下原始简历整理和改进表述，禁止编造学历、职位、项目职责、性能指标和成果。未知信息保留为空；待核实事项写入 sections 内的自定义模块内容中。只输出纯 JSON 对象，不要 Markdown 代码块。\n"
            +"保持现有 schema：title、templateId、color、fontSize(10–16)、lineHeight(1.2–2)、photo、profile{name,role,phone,email,city,link,summary}、sections[{title,content,visible}]。templateId 不得改变，sections 最多30项。content 支持 ## 标题 | 时间、- 列表、**加粗**、Markdown表格。不要插入 HTML。\n原始简历：\n"+JsonSerializer.Serialize(current,BackendClient.Json);
        var package=new DataPackage(); package.SetText(text); Clipboard.SetContent(package); Success("提示词已复制","将 AI 输出的 JSON 保存后，可在我的简历中导入。内容需要你核对。");
    }
    private async Task UpdateCapacity(WebView2 web)
    {
        if(web!=preview||capacityText is null||web.CoreWebView2 is null) return;
        try
        {
            string json=await web.CoreWebView2.ExecuteScriptAsync("(()=>{let a=document.querySelector('article');if(!a)return null;let top=a.getBoundingClientRect().top;let pad=parseFloat(getComputedStyle(a).paddingTop);let height=Math.max(...Array.from(a.children).map(x=>x.getBoundingClientRect().bottom-top-pad));let usable=269*96/25.4;return {pages:Math.max(1,Math.ceil(height/usable)),remaining:(usable-height)*25.4/96};})()");
            if(json=="null") return; using var doc=JsonDocument.Parse(json); int pages=doc.RootElement.GetProperty("pages").GetInt32(); double remaining=doc.RootElement.GetProperty("remaining").GetDouble();
            if(web!=preview||capacityText is null) return;
            capacityText.Text=pages>1?$"内容估算约 {pages} 页；超出单页约 {-remaining:0} mm，实际分页以 PDF 为准。":$"A4 单页内容余量约 {Math.Max(0,remaining):0} mm；实际分页以 PDF 为准。";
            capacityText.Foreground=Brush(pages>1||remaining<15?"#A56A05":"#167D78");
        }
        catch(Exception) { /* A navigation may replace the document while its size is being measured. */ }
    }
}
