using System.Text.RegularExpressions;

namespace ResumeStudio.Services;

public sealed record FitRequirement(string Name,string Requirement,string Evidence,string Status);
public sealed class ResumeSuggestion
{
    public int SectionIndex { get; set; }
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string Reason { get; set; } = "";
}
public static class JobFitAdvisor
{
    // Known skill aliases are bounded; evidence is a verbatim resume line, never a proficiency score.
    private static readonly (string Name,string Pattern)[] Skills=[
        ("C/C++",@"(?<![A-Za-z])C(?:\+\+|语言|语言程序设计)?(?![A-Za-z#])|C/C\+\+"),
        ("Linux",@"\bLinux\b"),("RTOS",@"\b(?:FreeRTOS|RTOS|RT-Thread)\b|实时操作系统"),
        ("STM32/MCU",@"\b(?:STM32\w*|MCU|ESP32)\b|单片机"),("ARM",@"\b(?:ARM|Cortex\w*|AArch64|RK356\w*)\b"),
        ("驱动开发",@"驱动|\bdriver\b"),("外设通信",@"\b(?:UART|SPI|I2C|CAN|RS485|RS232)\b|串口"),
        ("网络通信",@"\b(?:TCP|UDP|socket|epoll)\b|网络编程"),("调试",@"\b(?:GDB|JTAG|SWD)\b|调试|示波器|逻辑分析仪"),
        ("LVGL",@"\bLVGL\b"),("Git",@"\bGit\b"),("Python",@"\bPython\b"),("Java",@"\bJava\b|Spring"),
        ("Qt",@"\bQt\b"),("英语",@"CET[- ]?[46]|英语|English"),("学历",@"本科|硕士|博士|专科|学士")];
    public static List<FitRequirement> Analyze(Resume resume,Job job)
    {
        string[] requirements=Lines(job.Description); var evidence=resume.Sections.Where(s=>s.Visible).SelectMany(s=>Lines(s.Content).Select(l=>s.Title+"："+l)).ToArray();
        var found=new List<FitRequirement>();
        foreach(var skill in Skills)
        {
            var rx=new Regex(skill.Pattern,RegexOptions.IgnoreCase); string? requirement=requirements.FirstOrDefault(rx.IsMatch);
            if(requirement is null) continue; string? match=evidence.FirstOrDefault(rx.IsMatch);
            found.Add(new(skill.Name,requirement,match??"简历中未找到明确表述",match is null?"待确认":"有相关表述"));
        }
        return found;
    }
    private static string[] Lines(string text)=>text.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    public static List<ResumeSuggestion> LocalSuggestions(Resume resume,Job job)
    {
        var requirements=Analyze(resume,job); var result=new List<ResumeSuggestion>();
        for(int i=0;i<resume.Sections.Count;i++)
        {
            var section=resume.Sections[i]; if(!section.Visible||!section.Title.Contains("技能")) continue;
            var lines=Lines(section.Content); if(lines.Length<2) continue;
            int Weight(string line)=>requirements.Count(r=>Skills.Any(s=>s.Name==r.Name&&Regex.IsMatch(line,s.Pattern,RegexOptions.IgnoreCase)));
            var ordered=lines.Select((line,index)=>(line,index)).OrderByDescending(x=>Weight(x.line)).ThenBy(x=>x.index).Select(x=>x.line);
            string after=string.Join("\n",ordered);
            // Only reorder flat skill lists; structured headings and tables retain their original layout.
            if(lines.Any(l=>l.StartsWith("## ")||l.StartsWith('|'))||after==string.Join("\n",lines)) continue;
            result.Add(new() {SectionIndex=i,Before=section.Content,After=after,Reason="将职位要求涉及的技能放在前面，保留原有事实。"});
        }
        return result;
    }
    public static Resume Apply(Resume original,Job job,IEnumerable<ResumeSuggestion> suggestions)
    {
        var clone=System.Text.Json.JsonSerializer.Deserialize<Resume>(System.Text.Json.JsonSerializer.Serialize(original,BackendClient.Json),BackendClient.Json)!;
        var used=new HashSet<int>();
        foreach(var item in suggestions)
        {
            if(!used.Add(item.SectionIndex)||item.SectionIndex<0||item.SectionIndex>=clone.Sections.Count||clone.Sections[item.SectionIndex].Content!=item.Before||!clone.Sections[item.SectionIndex].Visible||item.After.Length>30000)
                throw new InvalidOperationException("建议与当前简历不一致，请重新分析。");
            clone.Sections[item.SectionIndex].Content=item.After;
        }
        clone.Id=Guid.NewGuid().ToString(); clone.Title=(original.Title+" · "+job.Company+" · "+job.Title); if(clone.Title.Length>280) clone.Title=clone.Title[..280];
        return clone;
    }
}
