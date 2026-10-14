using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResumeStudio.Services;

public static class PlatformCapture
{
    public const int MaxPayloadBytes=4_000_000;
    public const int MaxJobs=500;
    private static readonly JsonDocumentOptions JsonOptions=new() {MaxDepth=48};
    private static bool Domain(string host,string domain) => host==domain||host.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase);
    public static bool IsPlatformUrl(string provider,string url) => Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.UserInfo==""&&u.IsDefaultPort&&
        (provider=="boss"?Domain(u.Host,"zhipin.com"):provider=="nowcoder"&&Domain(u.Host,"nowcoder.com"));
    public static bool CanReadResponse(string provider,string url,string contentType) => IsJobResponseUrl(provider,url)&&contentType.Split(';')[0].Trim().EndsWith("json",StringComparison.OrdinalIgnoreCase);
    public static bool IsJobResponseUrl(string provider,string url)
    {
        if(!IsPlatformUrl(provider,url)) return false;
        string path=new Uri(url).AbsolutePath.ToLowerInvariant();
        if(Regex.IsMatch(path,@"login|passport|account|profile|captcha|verify|chat|conversation|security")) return false;
        return provider=="boss"?path.StartsWith("/wapi/zpgeek/")&&path.Contains("job"):Regex.IsMatch(path,@"job|recruit");
    }
    public static bool IsDetailUrl(string provider,string url)
    {
        if(!IsPlatformUrl(provider,url)) return false;
        string path=new Uri(url).AbsolutePath;
        return Regex.IsMatch(path,provider=="boss"?@"^/job_detail/[\w-]+\.html$":@"^/jobs/detail/[\w-]+/?$",RegexOptions.IgnoreCase);
    }
    public static bool IsChallenge(string title) => Regex.IsMatch(title,@"(?i)安全验证|人机验证|访问验证|登录验证|登录注册|Access denied|Checking your browser|Just a moment|captcha|^(登录|Login|请稍候)$");
    public static string FailureMessage(int statusCode,string json)
    {
        if(statusCode is 401 or 403) return $"职位接口拒绝访问（HTTP {statusCode}），请本人完成登录或验证后重新开启采集。";
        if(statusCode==429) return "职位接口请求过于频繁（HTTP 429），请稍后正常浏览并重新开启采集。";
        if(statusCode>=400) return $"职位接口请求失败（HTTP {statusCode}），请检查页面后重新开启采集。";
        if(statusCode!=200||string.IsNullOrWhiteSpace(json)||Encoding.UTF8.GetByteCount(json)>MaxPayloadBytes) return "";
        try
        {
            using var document=JsonDocument.Parse(json,JsonOptions); var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object) return "";
            string code=Value(root,"code");
            bool hasSuccess=root.TryGetProperty("success",out var success);
            bool failed=hasSuccess&&success.ValueKind==JsonValueKind.False||!(hasSuccess&&success.ValueKind==JsonValueKind.True)&&code!=""&&code is not ("0" or "200");
            if(!failed) return "";
            string detail=Clip(Plain(Value(root,"message","msg","errorMessage")),180);
            return "职位接口返回失败"+(code==""?"":"（"+Clip(code,30)+"）")+(detail==""?"":"："+detail)+"。请本人检查登录或验证状态后重新开启采集。";
        }
        catch(JsonException) {return "";}
    }
    public static Job Merge(Job previous,Job incoming)
    {
        incoming.Title=First(incoming.Title,previous.Title); incoming.Company=First(incoming.Company,previous.Company);
        incoming.Location=First(incoming.Location,previous.Location); incoming.Salary=First(incoming.Salary,previous.Salary);
        incoming.Experience=First(incoming.Experience,previous.Experience); incoming.PublishedAt=First(incoming.PublishedAt,previous.PublishedAt);
        if(previous.Description.Length>incoming.Description.Length) incoming.Description=previous.Description;
        if(incoming.Url==previous.Url&&incoming.Title==previous.Title&&incoming.Company==previous.Company&&incoming.Location==previous.Location&&incoming.Salary==previous.Salary&&incoming.Description==previous.Description&&incoming.Experience==previous.Experience&&incoming.PublishedAt==previous.PublishedAt) return previous;
        return incoming;
    }
    public static List<Job> ExtractJson(string provider,string responseUrl,string json)
    {
        if(!IsPlatformUrl(provider,responseUrl)||Encoding.UTF8.GetByteCount(json)>MaxPayloadBytes) return [];
        try
        {
            using var parsed=JsonDocument.Parse(json,JsonOptions);
            var found=new Dictionary<string,Job>(StringComparer.Ordinal); int visited=0;
            void Visit(JsonElement element,int depth)
            {
                if(depth>24||++visited>20000||found.Count>=MaxJobs) return;
                if(element.ValueKind==JsonValueKind.Object)
                {
                    var job=Candidate(provider,element);
                    if(job is not null) Add(found,job);
                    foreach(var property in element.EnumerateObject()) Visit(property.Value,depth+1);
                }
                else if(element.ValueKind==JsonValueKind.Array) foreach(var item in element.EnumerateArray()) Visit(item,depth+1);
            }
            Visit(parsed.RootElement,0); return found.Values.ToList();
        }
        catch(JsonException) { return []; }
    }
    private static Job? Candidate(string provider,JsonElement outer)
    {
        var data=Object(outer,"jobInfo")??outer;
        string title=Value(data,"jobName","jobTitle","positionName"),url,company=Value(data,"brandName","companyName","companyShortName");
        if(title==""||IsChallenge(title)) return null;
        if(provider=="boss")
        {
            string id=Value(data,"encryptJobId");
            if(!Regex.IsMatch(id,@"^[\w-]{1,180}$")) return null;
            url="https://www.zhipin.com/job_detail/"+id+".html";
            string security=Value(data,"securityId"); if(security!="") url+="?securityId="+Uri.EscapeDataString(security);
            company=First(company,Value(Object(outer,"brandComInfo"),"brandName","companyName"));
        }
        else if(provider=="nowcoder")
        {
            string id=Value(data,"jobId","id");
            bool evidence=Object(data,"recommendInternCompany") is not null||Object(data,"company") is not null||Value(data,"recruitType","companyId")!="";
            if(!evidence||!Regex.IsMatch(id,@"^\d{1,20}$")) return null;
            url="https://www.nowcoder.com/jobs/detail/"+id;
            company=First(company,Value(Object(data,"recommendInternCompany"),"companyName","companyShortName"),Value(Object(data,"company"),"companyName","name"));
        }
        else return null;
        string description=Value(data,"postDescription","jobDescription","description","jobDesc","responsibility");
        string ext=Value(data,"ext");
        if(ext!=""&&ext.Length<=100000)
        {
            try { using var extension=JsonDocument.Parse(ext,JsonOptions); description=First(description,string.Join("\n",new[] {Value(extension.RootElement,"infos"),Value(extension.RootElement,"requirements")}.Where(s=>s!=""))); }
            catch(JsonException) { }
        }
        string salary=Value(data,"salaryDesc","salaryShow","salary","salaryName");
        if(salary==""&&provider=="nowcoder")
        {
            string low=Value(data,"salaryMin"),high=Value(data,"salaryMax");
            if(low!=""&&low!="0") salary=low+(high!=""&&high!="0"&&high!=low?"-"+high:"")+(Value(data,"salaryType")=="1"?"元/天":"元/月");
        }
        var job=new Job {Title=Clip(title,250),Company=Clip(company,250),Url=url,Location=Clip(Value(data,"cityName","jobCity","location","city"),250),Salary=Clip(salary,100),Experience=Clip(Value(data,"jobExperience","experience","workYearName"),250),Description=Plain(description),PublishedAt=Value(data,"datePosted","publishTime","createTime")};
        Finish(provider,job,"浏览会话 JSON 采集，待核对"); return job;
    }
    public static List<Job> ExtractHtml(string provider,string pageUrl,string html)
    {
        if(!IsPlatformUrl(provider,pageUrl)||Encoding.UTF8.GetByteCount(html)>MaxPayloadBytes) return [];
        var title=Regex.Match(html,@"(?is)<title\b[^>]*>(.*?)</title>").Groups[1].Value;
        if(IsChallenge(Plain(title))) return [];
        var result=new Dictionary<string,Job>();
        foreach(Match script in Regex.Matches(html,@"(?is)<script\b([^>]*)>(.*?)</script>"))
        {
            string body=script.Groups[2].Value.Trim();
            var assignment=Regex.Match(body,@"(?:window\.)?(?:__INITIAL_STATE__|__NEXT_DATA__|__NUXT__|__PRELOADED_STATE__)\s*=\s*");
            if(assignment.Success) body=body[(assignment.Index+assignment.Length)..];
            else if(!Regex.IsMatch(script.Groups[1].Value,@"(?i)application/json|__NEXT_DATA__")) continue;
            try
            {
                var reader=new Utf8JsonReader(Encoding.UTF8.GetBytes(body),new JsonReaderOptions {MaxDepth=48});
                using var state=JsonDocument.ParseValue(ref reader);
                foreach(var job in ExtractJson(provider,pageUrl,state.RootElement.GetRawText())) Add(result,job);
            }
            catch(JsonException) { }
        }
        return result.Values.Take(MaxJobs).ToList();
    }
    public static List<Job> ExtractDom(string provider,string json)
    {
        if(Encoding.UTF8.GetByteCount(json)>MaxPayloadBytes) return [];
        try
        {
            using var document=JsonDocument.Parse(json,JsonOptions); var root=document.RootElement;
            if(!IsPlatformUrl(provider,Value(root,"url"))||IsChallenge(Value(root,"pageTitle"))) return [];
            if(!root.TryGetProperty("jobs",out var jobs)||jobs.ValueKind!=JsonValueKind.Array) return [];
            var found=new Dictionary<string,Job>();
            foreach(var item in jobs.EnumerateArray().Take(MaxJobs))
            {
                string url=Value(item,"url"),title=Value(item,"title");
                if(!IsDetailUrl(provider,url)||title==""||IsChallenge(title)) continue;
                var job=new Job {Url=url,Title=Clip(title,250),Company=Clip(Value(item,"company"),250),Location=Clip(Value(item,"location"),250),Salary=Clip(Value(item,"salary"),100),Description=Plain(Value(item,"description")),Experience=Clip(Value(item,"experience"),250)};
                if(job.Company==""&&job.Location==""&&job.Salary==""&&job.Description=="") continue;
                Finish(provider,job,"浏览页面采集，待核对"); Add(found,job);
            }
            return found.Values.ToList();
        }
        catch(JsonException) { return []; }
    }
    public static string Canonical(string url)
    {
        var u=new Uri(url); var tracking=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {"ka","sid","source","from","track","tracking","lid","securityid","channel","pagesource","deliversource"};
        string query=string.Join("&",u.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(q=> {string key=q.Split('=')[0]; return !key.StartsWith("utm_",StringComparison.OrdinalIgnoreCase)&&!tracking.Contains(key);}));
        return "https://"+u.Host.ToLowerInvariant()+u.AbsolutePath+(query==""?"":"?"+query);
    }
    private static void Finish(string provider,Job job,string status)
    {
        job.CanonicalUrl=Canonical(job.Url); job.Id="external-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(job.CanonicalUrl))).ToLowerInvariant()[..32]; job.Source=provider=="boss"?"BOSS直聘":"牛客"; job.InfoStatus=status;
    }
    private static void Add(Dictionary<string,Job> found,Job job)
    {
        if(!found.TryGetValue(job.CanonicalUrl,out var previous)||job.Description.Length>previous.Description.Length) found[job.CanonicalUrl]=job;
    }
    private static JsonElement? Object(JsonElement element,string name) => element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var property)&&property.ValueKind==JsonValueKind.Object?property:null;
    private static string Value(JsonElement? element,params string[] names)
    {
        if(element is not {} e||e.ValueKind!=JsonValueKind.Object) return "";
        foreach(string name in names) if(e.TryGetProperty(name,out var p)&&p.ValueKind is JsonValueKind.String or JsonValueKind.Number) {string value=p.ToString().Trim(); if(value!="") return value;}
        return "";
    }
    private static string First(params string[] values) => values.FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s))??"";
    private static string Clip(string value,int limit) => value.Length>limit?value[..limit]:value;
    private static string Plain(string value) => Clip(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(value,@"(?is)<(script|style)\b[^>]*>.*?</\1>",""),@"<[^>]*>"," ")).Trim(),30000);
}
