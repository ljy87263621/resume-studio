using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ResumeStudio.Services;

/// <summary>Preview and PDF share one document. All user content is escaped before formatting.</summary>
public static class ResumeRenderer
{
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string Inline(string text) => Regex.Replace(E(text), @"\*\*(.+?)\*\*", "<strong>$1</strong>");
    private static string Content(string text)
    {
        var b=new StringBuilder(); bool list=false;
        bool table=false;
        foreach(string line in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'))
        {
            if(line.Trim().StartsWith('|')&&line.Trim().EndsWith('|'))
            {
                if(list) { b.Append("</ul>"); list=false; }
                if(!table) { b.Append("<table>"); table=true; }
                var cells=line.Trim().Trim('|').Split('|');
                if(cells.All(c=>Regex.IsMatch(c.Trim(),"^:?-{3,}:?$"))) continue;
                b.Append("<tr>"); foreach(var cell in cells) b.Append("<td>").Append(Inline(cell.Trim())).Append("</td>"); b.Append("</tr>"); continue;
            }
            if(table) { b.Append("</table>"); table=false; }
            bool bullet=line.StartsWith("- ")||line.StartsWith("• ");
            if(bullet&&!list) { b.Append("<ul>"); list=true; }
            if(!bullet&&list) { b.Append("</ul>"); list=false; }
            if(bullet) b.Append("<li>").Append(Inline(line[2..])).Append("</li>");
            else if(line.StartsWith("## "))
            {
                var parts=line[3..].Split(" | ",2); b.Append("<h3><span>").Append(Inline(parts[0])).Append("</span>");
                if(parts.Length>1) b.Append("<small>").Append(Inline(parts[1])).Append("</small>"); b.Append("</h3>");
            }
            else if(!string.IsNullOrWhiteSpace(line)) b.Append("<p>").Append(Inline(line)).Append("</p>");
        }
        if(list) b.Append("</ul>"); if(table) b.Append("</table>"); return b.ToString();
    }
    public static string Html(Resume r,Template t,bool photoPlaceholder=false)
    {
        string color=Regex.IsMatch(r.Color??"", "^#[0-9a-fA-F]{6}$")?r.Color!:t.Color;
        var p=r.Profile;
        var contact=string.Join(" · ",new[] {p.Phone,p.Email,p.City,p.Link}.Where(s=>!string.IsNullOrWhiteSpace(s)).Select(E));
        string photo=Regex.IsMatch(r.Photo??"",@"^data:image/(png|jpeg);base64,[A-Za-z0-9+/=]+$")?$"<img class='photo' src='{r.Photo}' alt='证件照'>":"";
        if(photo.Length==0&&photoPlaceholder&&t.SupportsPhoto) photo="<div class='photo photo-placeholder' role='img' aria-label='证件照占位'><svg viewBox='0 0 64 64' aria-hidden='true'><circle cx='32' cy='21' r='11'/><path d='M12 58v-5c0-15 40-15 40 0v5z'/></svg><span>证件照</span></div>";
        string Section(ResumeSection s) => $"<section><h2>{E(s.Title)}</h2>{Content(s.Content)}</section>";
        var visible=r.Sections.Where(s=>s.Visible&&!string.IsNullOrWhiteSpace(s.Content)).ToList();
        var sections=t.Layout=="deedy"?$"<div class='columns'><aside>{string.Join("",visible.Where(s=>t.SideTitles.Contains(s.Title)).Select(Section))}</aside><main>{string.Join("",visible.Where(s=>!t.SideTitles.Contains(s.Title)).Select(Section))}</main></div>":string.Join("",visible.Select(Section));
        var size=Math.Clamp(r.FontSize,10,16).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var line=Math.Clamp(r.LineHeight,1.2,2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string font=ResumeTypography.Normalize(r.FontFamily);
        return $$"""
        <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>{{E(r.Title)}}</title>
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; font-src https://resumestudio.assets; style-src 'unsafe-inline';">
        <style>
        @page{size:A4;margin:14mm 16mm} *{box-sizing:border-box} body{margin:0;background:#e9edf4;color:#202b3d;font-family:'Microsoft YaHei','Segoe UI',sans-serif;font-size:{{size}}pt;line-height:{{line}}}
        article{background:white;max-width:210mm;min-height:297mm;margin:22px auto;padding:14mm 16mm;box-shadow:0 5px 25px #18264015}
        header{border-bottom:3px solid {{color}};padding-bottom:16px;margin-bottom:20px;min-height:80px} h1{font-size:29px;line-height:1.3;margin:0 0 6px;color:{{color}}} .role{font-size:16px;font-weight:600;margin:0 0 7px}.contact{font-size:11px;color:#566174;overflow-wrap:anywhere} .summary{font-size:12px;margin-top:10px}
        .photo{float:right;object-fit:cover;width:65px;height:86px;margin-left:15px}h2{color:{{color}};font-size:15px;padding:5px 0;border-bottom:1px solid {{color}};margin:18px 0 9px;break-after:avoid} h3{font-size:{{size}}pt;margin:10px 0 4px;break-after:avoid}p{margin:4px 0;white-space:pre-wrap;overflow-wrap:anywhere}ul{padding-left:19px;margin:5px 0}li{margin:3px 0;overflow-wrap:anywhere}section{break-inside:auto}li,p{orphans:2;widows:2} strong{font-weight:700}
        .sidebar header{border:0;background:{{color}};color:white;padding:20px;margin:-14mm -16mm 22px}.sidebar h1,.sidebar .contact{color:white}.sidebar h2{border-left:4px solid {{color}};padding-left:10px;border-bottom:0;background:#f2f6f7}
        .band h2{background:{{color}};color:white;border:0;border-radius:3px;padding:5px 10px}.minimal header{border-bottom:1px solid #222}.minimal h1{color:#222}.minimal h2{color:#222;border-bottom:1px solid #bbb}
        h3{display:flex;justify-content:space-between;gap:14px}h3 small{font-size:10pt;font-weight:400;color:#666;flex-shrink:0}table{width:100%;border-collapse:collapse;margin:8px 0;font-size:11pt}td{border:1px solid #ccd3dd;padding:5px 8px;overflow-wrap:anywhere}tr:first-child{background:#f1f4f8;font-weight:600}
        header::after{content:'';display:table;clear:both}.photo-placeholder{border:1px dashed #8b99af;border-radius:3px;background:#edf2f8;color:#73839c;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;font-family:'Microsoft YaHei',sans-serif;font-size:10px}.photo-placeholder svg{width:42px;height:48px;fill:#a7b5c9}.photo-placeholder span{line-height:1.2}
        /* Maintained HTML/CSS ports: billryan and Jake MIT; Awesome-CV LPPL-1.3c;
           MiSans is provided unchanged under Xiaomi's font license. */
        @font-face{font-family:MiSans;src:url('https://resumestudio.assets/Fonts/MiSans-Regular.ttf') format('truetype');font-weight:400;font-display:block}
        :root{--resume-font:'{{font}}'}article,article header,article h1,article h2,article h3,article p,article li,article td,article .photo-placeholder{font-family:var(--resume-font),MiSans,'Microsoft YaHei',sans-serif!important}
        /*
           Deedy Apache-2.0. Upstream copyrights retained in ThirdParty.
           Adapted by scj040921, 2026-10-12. See THIRD_PARTY_NOTICES.md. */
        .billryan{font-family:'Times New Roman','SimSun',serif}.billryan header{text-align:center;border:0;margin-bottom:10px;padding-bottom:5px}.billryan h1{font-size:34px;color:#222;font-weight:600}.billryan .role{font-size:13px;font-weight:400}.billryan h2{font-size:18px;color:#222;border-bottom:1px solid #222;margin:18px 0 8px;padding:0 0 3px;font-weight:400}.billryan h3{font-size:14px;font-weight:600}.billryan li{margin:1px 0}
        .jake{font-family:'Times New Roman','SimSun',serif}.jake header{text-align:center;border:0;padding-bottom:0;margin-bottom:12px}.jake h1{font-size:32px;color:#111}.jake .role{font-size:13px;font-weight:400}.jake h2{font-size:17px;color:#111;border-bottom:1px solid #111;margin:13px 0 6px;padding:0 0 3px;font-weight:400}.jake li{margin:0}.jake h3{font-size:13px;margin:7px 0 2px}
        .awesome header{text-align:center;border:0;margin-bottom:12px;padding-bottom:0}.awesome h1{font-size:36px;color:#333}.awesome .role{font-size:11px;color:{{color}};letter-spacing:1px}.awesome h2{font-size:18px;color:{{color}};border-color:#ccc;margin:18px 0 8px;padding:0 0 3px}.awesome h3{font-size:14px;color:#333}.awesome p{margin:3px 0}.awesome li{margin:1px 0}
        .deedy header{text-align:center;border-bottom:1px solid #6a6a6a;margin-bottom:18px;padding-bottom:12px}.deedy h1{font-size:42px;color:#2b2b2b;font-weight:300}.deedy .role{color:#666;font-size:12px;font-weight:400}.deedy .columns{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,2fr);gap:22px}.deedy h2{border:0;color:#6a6a6a;font-size:18px;font-weight:400;padding:0;margin:12px 0 6px}.deedy h3{font-size:13px}.deedy aside{font-size:10.5pt}.deedy aside h3{display:block}.deedy aside h3 small{display:block;font-size:9pt}.deedy aside li{margin:1px 0}.deedy main{min-width:0}
        @media print{body{background:white}article{width:auto;min-height:0;margin:0;padding:0;box-shadow:none}.sidebar header{margin:0 0 22px}.photo{-webkit-print-color-adjust:exact}*{-webkit-print-color-adjust:exact;print-color-adjust:exact} }
        </style></head><body><article class="{{t.Layout}}"><header>{{photo}}<h1>{{E((string.IsNullOrWhiteSpace(p.Name)?"你的姓名":p.Name))}}</h1><div class="role">{{E(p.Role)}}</div><div class="contact">{{contact}}</div><div class="summary">{{Content(p.Summary)}}</div></header>{{sections}}</article></body></html>
        """;
    }
}
