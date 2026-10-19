using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeStudio;

public sealed class Template
{
    public string Id { get; set; } = "campus";
    public string Name { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Color { get; set; } = "#245AE8";
    public string Layout { get; set; } = "classic";
    public string Description { get; set; } = "";
    public List<string> Sections { get; set; } = [];
    public string Category { get; set; } = "通用型";
    public string Origin { get; set; } = "builtin";
    public string SourceRepo { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public int Stars { get; set; }
    public string StarsCheckedAt { get; set; } = "";
    public string License { get; set; } = "";
    public string Adaptation { get; set; } = "";
    public List<string> SideTitles { get; set; } = [];
    public bool SupportsPhoto { get; set; } = true;
    public override string ToString() => Name;
}
public sealed class Profile
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string City { get; set; } = "";
    public string Link { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<ProfileItem> Items { get; set; } = [];
}
public sealed class ProfileItem
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Emoji { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}
public sealed class ResumeImage
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DataUrl { get; set; } = "";
    public string Alt { get; set; } = "";
    public string Caption { get; set; } = "";
    public double WidthPercent { get; set; } = 100;
    public double MaxHeightMm { get; set; } = 60;
}
public sealed class ResumeSection
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Visible { get; set; } = true;
    public List<ResumeImage> Images { get; set; } = [];
    [JsonIgnore] public string EditorStatus=>!Visible?"已隐藏":string.IsNullOrWhiteSpace(Content)?"未填":Content.Contains("[填写")||Content.Contains("示例")||Content.Contains("待补充")?"待优化":"已填";
    [JsonIgnore] public string StatusColor=>EditorStatus=="已填"?"#167D78":EditorStatus=="待优化"?"#A56A05":"#77849A";
    public override string ToString() => "≡  "+Title+" · "+EditorStatus;
}
public sealed class Resume
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "我的简历";
    public string TemplateId { get; set; } = "campus";
    public string Color { get; set; } = "#245AE8";
    public double FontSize { get; set; } = 12;
    public double LineHeight { get; set; } = 1.6;
    public string FontFamily { get; set; } = "MiSans";
    public double PageMarginVertical { get; set; } = 14;
    public double PageMarginHorizontal { get; set; } = 16;
    public double SectionSpacing { get; set; } = 4.8;
    public string HeaderAlignment { get; set; } = "template";
    public string Photo { get; set; } = "";
    public Profile Profile { get; set; } = new();
    public List<ResumeSection> Sections { get; set; } = [];
    public string UpdatedAt { get; set; } = "";
}
public sealed class Job
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = "";
    public string Tags {get;set;}="";
    public string DeadlineAt {get;set;}="";
    public string LastFollowedAt {get;set;}="";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string Source { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public string Experience { get; set; } = "";
    public string Salary { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string Notes { get; set; } = "";
    public string InfoStatus { get; set; } = "";
}
public sealed class ResumeCheck
{
    public List<ModuleCheck> Modules { get; set; } = [];
    public List<string> Tips { get; set; } = [];
}
public sealed class ModuleCheck
{
    public string Title { get; set; } = "";
    public string Status { get; set; } = "未填";
    public List<string> Suggestions { get; set; } = [];
}
public sealed class CapturePreview
{
    public Job Job { get; set; } = new();
    public string Message { get; set; } = "";
    public string Status { get; set; } = "";
}
public sealed class ApplicationRecord
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string JobId { get; set; } = "";
    public string Tags {get;set;}="";
    public string DeadlineAt {get;set;}="";
    public string LastFollowedAt {get;set;}="";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Url { get; set; } = "";
    public string Status { get; set; } = "已打开";
    public string ResumeTitle { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string UpdatedAt { get; set; } = "";
    public Resume? ResumeSnapshot { get; set; }
    public List<ApplicationEvent> Timeline { get; set; } = [];
    public string EventKind { get; set; } = "面试";
    public string DueAt { get; set; } = "";
    public bool ReminderDone { get; set; }
    public string Review { get; set; } = "";
}
public sealed class CollectionResult<T>
{
    public T Record { get; set; } = default!;
    public bool Created { get; set; }
    public bool Updated { get; set; }
}
public sealed class ScheduleItem
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "自定义";
    public string StartAt { get; set; } = "";
    public bool AllDay { get; set; }
    public bool Completed { get; set; }
    public bool ReminderEnabled { get; set; } = true;
    public int ReminderMinutes { get; set; } = 1440;
    public string Notes { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
public sealed class ApplicationEvent
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Status { get; set; } = "";
    public string At { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class ResumeRevision
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string Label { get; set; } = "";
    public Resume? Resume { get; set; }
}
public sealed class JobResult
{
    public List<Job> Items { get; set; } = [];
    public int Total { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string FetchedAt { get; set; } = "";
    public bool Cached { get; set; }
    public int Page { get; set; }
    public List<CompanySourceStatus> Sources { get; set; } = [];
}
public sealed class CompanySource
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Adapter { get; set; } = "jsonld";
    public string Slug { get; set; } = "";
    public string Url { get; set; } = "";
    public string Region { get; set; } = "国内";
    public bool Enabled { get; set; } = true;
    public bool Builtin { get; set; }
    public string Status { get; set; } = "未查询";
    public string Message { get; set; } = "";
    public int LastCount { get; set; }
    public string LastQueryAt { get; set; } = "";
    public override string ToString() => Name;
}
public sealed class CompanySourceStatus
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int LastCount { get; set; }
    public string LastQueryAt { get; set; } = "";
}
