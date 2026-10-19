using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace ResumeStudio.Services;

/// <summary>Portable document helpers shared by the editor, HTML renderer and regression checks.</summary>
public static class ResumeLayout
{
    public static readonly string[] FontNames=["MiSans","Microsoft YaHei","SimSun","Arial","Times New Roman"];
    public static string Font(string? name)=>FontNames.Contains(name)?name!:"MiSans";
    public static string Number(double value,double fallback,double minimum,double maximum)=>Math.Clamp(double.IsFinite(value)?value:fallback,minimum,maximum).ToString("0.###",CultureInfo.InvariantCulture);
    public static readonly Regex ImageReference=new(@"^!\[图片\]\(resume-image:([A-Za-z0-9_-]{1,80})\)$",RegexOptions.Compiled);
    public static string ImageToken(ResumeImage image)=>$"![图片](resume-image:{image.Id})";
    public static bool ValidImage(string? data)=>data is {Length:>0 and <=1_000_000}&&Regex.IsMatch(data,@"^data:image/(png|jpeg|webp);base64,[A-Za-z0-9+/]+={0,2}$")&&ValidBase64(data[(data.IndexOf(',')+1)..]);
    private static bool ValidBase64(string text){try{return Convert.FromBase64String(text).Length>0;}catch(FormatException){return false;}}
    public static void ReplaceImage(Resume resume,ResumeImage image,string data,JsonSerializerOptions options)
    {
        if(!resume.Sections.Any(s=>s.Images.Contains(image)))throw new IOException("简历或图片已切换，请重新选择图片。");
        if(!ValidImage(data))throw new IOException("图片数据无效或过大。");
        string original=image.DataUrl;
        try {
            image.DataUrl=data;
            if(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(resume,options))>1_850_000)throw new IOException("替换后的简历接近 2 MB 上限，请缩小图片后重试。");
        }catch{image.DataUrl=original;throw;}
    }
    public static bool HasContent(ResumeSection section)=>section.Content.Replace("\r\n","\n").Replace('\r','\n').Split('\n').Any(line=> {
        var match=ImageReference.Match(line.Trim());
        return match.Success?section.Images.Any(i=>i.Id==match.Groups[1].Value&&(ValidImage(i.DataUrl)||!string.IsNullOrWhiteSpace(i.Caption))):!string.IsNullOrWhiteSpace(line);
    });
    public static string List(string text)=>string.Join("\n",Lines(text).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>"- "+Regex.Replace(s.Trim(),@"^[-•] ","")));
    public static string Experience(string name,string role,string start,string end,string description)=>
        "## "+name.Trim().Replace('\n',' ')+(string.IsNullOrWhiteSpace(start)&&string.IsNullOrWhiteSpace(end)?"":" | "+string.Join(" – ",new[] {start.Trim(),end.Trim()}.Where(s=>s.Length>0)))+
        (string.IsNullOrWhiteSpace(role)?"":"\n"+role.Trim())+(string.IsNullOrWhiteSpace(description)?"":"\n"+List(description));
    public static string[] Lines(string text)=>text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
    private static string Cell(string text)=>text.Replace("&","&amp;").Replace("\\","\\\\").Replace("|","\\|").Replace("\r\n","\n").Replace('\r','\n').Replace("\n","&#10;");
    public static string Table(IReadOnlyList<List<string>> rows)
    {
        if(rows.Count==0)return "";
        int columns=rows.Max(r=>r.Count);if(columns==0)return "";
        string Row(List<string> row)=>"| "+string.Join(" | ",Enumerable.Range(0,columns).Select(i=>Cell(i<row.Count?row[i]:"")))+" |";
        return Row(rows[0])+"\n| "+string.Join(" | ",Enumerable.Repeat("---",columns))+" |"+(rows.Count==1?"":"\n"+string.Join("\n",rows.Skip(1).Select(Row)));
    }
    public static bool IsTableLine(string text)=>text.Trim().StartsWith('|')&&text.Trim().EndsWith('|')&&text.Trim().Length>=2;
    public static List<string> Cells(string line)
    {
        var cells=new List<string>();var cell=new StringBuilder();string text=line.Trim()[1..^1];
        for(int i=0;i<text.Length;i++) {
            if(text[i]=='\\'&&i+1<text.Length&&text[i+1] is '\\' or '|'){cell.Append(text[++i]);continue;}
            if(text[i]=='|'){cells.Add(WebUtility.HtmlDecode(cell.ToString().Trim()));cell.Clear();}else cell.Append(text[i]);
        }
        cells.Add(WebUtility.HtmlDecode(cell.ToString().Trim()));return cells;
    }
    public static bool Separator(List<string> row)=>row.All(c=>Regex.IsMatch(c.Trim(),"^:?-{3,}:?$"));
    public sealed record TableRange(int Start,int Length,List<List<string>> Rows);
    public static TableRange? ReadTable(string content,int caret)
    {
        var lines=Lines(content);int offset=0,first=-1,start=0;
        for(int i=0;i<=lines.Length;i++) {
            bool table=i<lines.Length&&IsTableLine(lines[i]);
            if(table&&first<0){first=i;start=offset;}
            if(!table&&first>=0) {
                int length=offset-start-1;
                if(caret>=start&&caret<=start+length)return new(start,length,lines[first..i].Select(Cells).Where(row=>!Separator(row)).ToList());
                first=-1;
            }
            if(i<lines.Length)offset+=lines[i].Length+1;
        }
        return null;
    }
}
