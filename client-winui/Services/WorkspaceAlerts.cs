namespace ResumeStudio.Services;
public static class WorkspaceAlerts
{
    public static string For(string deadline,string last,string status=""){
        var parts=new List<string>();var now=DateTimeOffset.Now;
        if(DateTimeOffset.TryParse(deadline,out var due)&&due<now.AddDays(3))parts.Add(due<now?"已过截止时间":"三天内截止");
        if(status!=""&&status is not "已录用" and not "已结束"&&DateTimeOffset.TryParse(last,out var followed)&&followed<now.AddDays(-7))parts.Add("超过七天未跟进");
        return string.Join(" · ",parts);
    }
}
