using System.Text.RegularExpressions;

namespace ResumeStudio.Services;

public static class ResumeEditing
{
    public static (string Text,int Start,int Length) Format(string text,int start,int length,string kind)
    {
        start=Math.Clamp(start,0,text.Length); length=Math.Clamp(length,0,text.Length-start);
        if(kind=="bold")
        {
            string selected=length==0?"重点内容":text.Substring(start,length);
            string replacement=selected.StartsWith("**")&&selected.EndsWith("**")&&selected.Length>=4?selected[2..^2]:"**"+selected+"**";
            return (text[..start]+replacement+text[(start+length)..],start,replacement.Length);
        }
        if(kind=="table")
        {
            string table="\n| 项目 | 内容 |\n| --- | --- |\n| 名称 | 请填写 |\n";
            return (text[..start]+table+text[(start+length)..],start,table.Length);
        }
        int from=start==0?0:text.LastIndexOf('\n',start-1)+1;
        int end=length>0&&text[start+length-1]=='\n'?start+length-1:text.IndexOf('\n',start+length);
        if(end<0) end=text.Length;
        string block=text[from..end]; string prefix=kind=="heading"?"## ":"- ";
        var lines=block.Split('\n'); bool remove=lines.All(l=>l.StartsWith(prefix));
        string result=string.Join("\n",lines.Select(l=>remove?l[prefix.Length..]:prefix+Regex.Replace(l,@"^(## |[-•] )","")));
        return (text[..from]+result+text[end..],from,result.Length);
    }
}
