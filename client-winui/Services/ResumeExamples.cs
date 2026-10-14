using System.Text.Json;

namespace ResumeStudio.Services;

/// <summary>Sample content belongs to the preview, never the stored resume or PDF.</summary>
public static class ResumeExamples
{
    public static Resume Blank(Template template) => new() {
        Title=template.Name+"简历",TemplateId=template.Id,Color=template.Color,
        FontSize=template.Origin=="github"?11:12,LineHeight=template.Origin=="github"?1.45:1.6,
        Sections=template.Sections.Select(title=>new ResumeSection {Title=title}).ToList()
    };

    public static Resume Create(Template template)
    {
        var sample=Blank(template);
        sample.Title="模板预览示例";
        sample.Profile=new Profile {
            Name="示例姓名",Role=template.Category=="通用型"?"软件工程师":template.Category+"工程师",
            Phone="138 0000 0000",Email="example@email.com",City="期望城市",
            Summary="内容仅用于展示版式，请按真实经历填写。"
        };
        foreach(var section in sample.Sections) section.Content=SectionContent(section.Title,template.Category);
        return sample;
    }

    public static string SectionContent(string title,string category)
    {
        if(title.Contains("教育")) return "## 示例大学 · 计算机相关专业 | 2023.09 - 2027.06\n本科\n- 相关课程：数据结构、操作系统、计算机网络。";
        if(title.Contains("技能")) return category switch {
            "前端开发"=>"- 语言与框架：[填写 JavaScript / TypeScript、Vue / React 的实际掌握情况]\n- 工程实践：[填写组件、构建工具、兼容性和测试经验]",
            "后端开发"=>"- 语言与服务：[填写 Java / Go / Python、框架与接口开发经验]\n- 存储与可靠性：[填写数据库、缓存、协议和测试经验]",
            "嵌入式开发"=>"- 语言与系统：[填写 C / C++、RTOS / Linux 的实际掌握情况]\n- 平台与调试：[填写 MCU / SoC、接口协议、调试工具]",
            _=>"- 编程语言：[填写实际掌握的技术]\n- 工具与实践：[填写目标岗位相关能力]"
        };
        if(title.Contains("作品")) return "- 个人主页：[填写作品链接]\n- 开源贡献：[填写仓库与实际贡献]";
        return $"## {title}示例 | 时间范围\n角色 / 技术栈 / 平台\n- 说明你负责的设计、开发或调试工作。\n- 描述可核实的成果与 **验证方式**，不编造指标。";
    }

    public static Resume ForPreview(Resume resume,Template template)
    {
        // A deep copy also keeps photo, style, hidden modules and manual ordering intact.
        var preview=JsonSerializer.Deserialize<Resume>(JsonSerializer.Serialize(resume))!;
        var sample=Create(template).Profile;
        string Fill(string value,string fallback)=>string.IsNullOrWhiteSpace(value)?fallback:value;
        var profile=preview.Profile;
        profile.Name=Fill(profile.Name,sample.Name);profile.Role=Fill(profile.Role,sample.Role);
        profile.Phone=Fill(profile.Phone,sample.Phone);profile.Email=Fill(profile.Email,sample.Email);
        profile.City=Fill(profile.City,sample.City);profile.Summary=Fill(profile.Summary,sample.Summary);
        foreach(var section in preview.Sections)
            if(string.IsNullOrWhiteSpace(section.Content)) section.Content=SectionContent(section.Title,template.Category);
        return preview;
    }

    public static bool HasExamples(Resume resume) =>
        new[] {resume.Profile.Name,resume.Profile.Role,resume.Profile.Phone,resume.Profile.Email,resume.Profile.City,resume.Profile.Summary}.Any(string.IsNullOrWhiteSpace)
        ||resume.Sections.Any(section=>section.Visible&&string.IsNullOrWhiteSpace(section.Content));
}
