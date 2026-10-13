namespace ResumeStudio.Services;

public static class JobWorkflow
{
    public static readonly string[] Stages=["已打开","已投递","笔试中","面试中","已录用","已结束"];
    public static bool Closed(ApplicationRecord record)=>record.Status is "已录用" or "已结束";
    public static bool Pending(ApplicationRecord record)=>!record.ReminderDone&&!Closed(record)&&DateTimeOffset.TryParse(record.DueAt,out _);
    public static void ValidateLink(string value)
    {
        if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||string.IsNullOrEmpty(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("请输入完整的 HTTPS 职位链接。");
    }
}
