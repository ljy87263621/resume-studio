using ResumeStudio;
using ResumeStudio.Services;

int checks=0;
void Check(bool value,string label) { if(!value) throw new Exception(label); checks++; Console.WriteLine("PASS: "+label); }
const string boss="https://www.zhipin.com/wapi/zpgeek/search/joblist.json";
const string nowcoder="https://www.nowcoder.com/jobs/intern/center";
var jobs=PlatformCapture.ExtractJson("boss",boss,"""
{"code":0,"zpData":{"jobList":[{"encryptJobId":"abc123","jobName":"C++工程师","brandName":"示例公司","cityName":"深圳","salaryDesc":"20-30K","jobExperience":"1-3年","securityId":"access-token"}]}}
""");
Check(jobs.Count==1&&jobs[0].Title=="C++工程师"&&jobs[0].Company=="示例公司","BOSS zpData jobList fields are normalized");
Check(jobs[0].Url.Contains("securityId=access-token")&&!jobs[0].CanonicalUrl.Contains("securityId"),"BOSS opening parameter is separate from canonical identity");
Check(PlatformCapture.ExtractJson("boss","https://www.zhipin.com.evil.test/wapi/zpgeek/joblist.json","{}").Count==0,"lookalike response domains are rejected");
Check(!PlatformCapture.CanReadResponse("boss","https://www.zhipin.com/wapi/zppassport/login.json","application/json"),"login and credential responses are excluded");
Check(!PlatformCapture.CanReadResponse("nowcoder","https://www.nowcoder.com/nccommon/user/profile","application/json"),"profile responses are excluded");
Check(!PlatformCapture.CanReadResponse("boss",boss,"image/png"),"non-JSON response bodies are excluded");
var nc=PlatformCapture.ExtractJson("nowcoder",nowcoder,"""
{"store":{"interCenter":{"jobList":[{"data":{"id":467898,"jobName":"产品经理","recruitType":2,"jobCity":"成都","salaryMin":300,"salaryMax":500,"salaryType":1,"recommendInternCompany":{"companyName":"示例企业"},"ext":"{\"infos\":\"岗位职责：产品设计\",\"requirements\":\"本科及以上\"}"}}]}}}
""");
Check(nc.Count==1&&nc[0].Url=="https://www.nowcoder.com/jobs/detail/467898"&&nc[0].Salary=="300-500元/天","Nowcoder actual nested data shape and daily salary are normalized");
Check(nc[0].Company=="示例企业"&&nc[0].Description.Contains("本科及以上"),"Nowcoder nested company and ext requirements are retained");
var html="<script>window.__INITIAL_STATE__={\"store\":{\"jobList\":[{\"id\":12,\"jobName\":\"芯片设计\",\"recruitType\":1,\"companyName\":\"芯片公司\"}]}};</script>";
Check(PlatformCapture.ExtractHtml("nowcoder",nowcoder,html).Count==1,"public HTML JSON state is parsed without executing scripts");
Check(PlatformCapture.ExtractHtml("boss","https://www.zhipin.com/web/geek/jobs","<title>安全验证</title>"+html).Count==0,"challenge HTML cannot become a job");
Check(PlatformCapture.ExtractJson("boss",boss,"{broken").Count==0,"invalid JSON is ignored without interrupting browsing");
Check(PlatformCapture.ExtractJson("boss",boss,new string('x',PlatformCapture.MaxPayloadBytes+1)).Count==0,"oversized responses are rejected");
Check(PlatformCapture.ExtractJson("nowcoder",nowcoder,"{\"jobName\":\"推广文章\",\"id\":1}").Count==0,"unrelated title and ID objects are not jobs");
var duplicate=PlatformCapture.ExtractJson("boss",boss,"""
{"zpData":{"jobList":[{"encryptJobId":"dup","jobName":"开发","brandName":"公司"},{"encryptJobId":"dup","jobName":"开发","brandName":"公司"}]}}
""");
Check(duplicate.Count==1,"duplicate responses collapse by canonical URL");
var dom=PlatformCapture.ExtractDom("nowcoder","""
{"url":"https://www.nowcoder.com/jobs/intern/center","pageTitle":"牛客实习广场","jobs":[{"url":"https://www.nowcoder.com/jobs/detail/12?channel=npJobTab","title":"产品实习生","company":"公司","salary":"面议"},{"url":"https://www.nowcoder.com/enterprise/12","title":"企业推广","company":"公司"}]}
""");
Check(dom.Count==1&&dom[0].CanonicalUrl=="https://www.nowcoder.com/jobs/detail/12","DOM only accepts job-detail links and removes tracking");
Check(PlatformCapture.ExtractDom("boss","{\"url\":\"https://www.zhipin.com/web/geek/jobs\",\"pageTitle\":\"安全验证\",\"jobs\":[]}").Count==0,"verification pages leave the capture empty");
var regressionFailures=new List<string>();
void Regression(bool condition,string label) {if(!condition) regressionFailures.Add(label); else Check(true,label);}
var merge=typeof(PlatformCapture).GetMethod("Merge");
var summary=new Job {Company="公司",Description="",Experience="本科",PublishedAt="2026-10-14"};
var incoming=new Job {Company="公司",Description="",Salary="20-30K",Location="深圳"};
var enriched=merge?.Invoke(null,new object[] {summary,incoming}) as Job;
Regression(enriched?.Salary=="20-30K"&&enriched.Location=="深圳"&&enriched.Experience=="本科"&&enriched.PublishedAt=="2026-10-14","later JSON enriches DOM fields without losing prior experience or publication time");
var failure=typeof(PlatformCapture).GetMethod("FailureMessage");
string Failure(int code,string body) => failure?.Invoke(null,new object[] {code,body}) as string??"";
Regression(Failure(401,"")!=""&&Failure(403,"")!=""&&Failure(429,"")!="","HTTP auth denied and throttled job responses report a failure");
Regression(Failure(200,"{\"code\":37,\"message\":\"请先登录\"}")!="","JSON business login failure is reported");
Regression(failure is not null&&Failure(200,"{\"code\":0,\"zpData\":{\"jobList\":[]}} ")==""&&Failure(200,"{\"code\":0,\"zpData\":{\"jobList\":[{\"code\":37}]}} ")==""&&Failure(200,"{\"code\":200,\"data\":[]}")=="","normal empty pages and nested job codes are not failures");
var jobResponse=typeof(PlatformCapture).GetMethod("IsJobResponseUrl");
Regression(jobResponse?.Invoke(null,new object[] {"boss",boss}) is true&&jobResponse.Invoke(null,new object[] {"boss","https://www.zhipin.com/wapi/zppassport/login.json"}) is false,"job response URLs can be classified before an HTML denial response MIME is inspected");
Regression(Failure(200,"{\"code\":1,\"success\":true,\"data\":[]}")=="","explicit successful business responses do not pause capture");
if(regressionFailures.Count>0) throw new Exception(string.Join("\n",regressionFailures));
var release=new TaskCompletionSource();int sends=0;bool fail=true;
var sink=new CapturedFavorites(async job=>{sends++;if(sends==1)await release.Task;if(job.Id=="second"&&fail)throw new IOException("fixture disk full");return new CollectionResult<Job> {Record=job,Created=job.Id!="existing"};});
Job Captured(string id)=>new() {Id=id,CanonicalUrl="https://www.nowcoder.com/jobs/detail/"+id,Url="https://www.nowcoder.com/jobs/detail/"+id,Title=id,Company="fixture"};
sink.Accept(Captured("first"));var firstSave=sink.SaveAsync();sink.Accept(Captured("first"));sink.Accept(Captured("second"));sink.Accept(Captured("existing"));
Check(ReferenceEquals(firstSave,sink.SaveAsync())&&sink.Busy,"overlapping capture events share one ordered save operation");
release.SetResult();await firstSave;
Check(sink.Added==1&&sink.Pending==2&&sink.Error.Contains("disk full")&&!sink.Busy,"failed persistence retains unsaved jobs and acknowledges only committed favorites");
int failedSends=sends;await sink.SaveAsync();Check(sends==failedSends,"failed automatic save waits for explicit retry instead of spinning");
fail=false;await sink.SaveAsync(true);Check(sink.Added==2&&sink.Existing==1&&sink.Pending==0&&sink.Error=="","retry drains remaining jobs without resaving successful ones");
sink.Accept(Captured("first"));sink.Accept(Captured("second"));await sink.SaveAsync();Check(sends==4,"repeated DOM reads do not create repeated write requests");
const string current="""
{"url":"https://www.zhipin.com/web/geek/jobs","pageTitle":"职位","targetUrl":"https://www.zhipin.com/job_detail/current.html","detailVisible":true,"jobs":[{"url":"https://www.zhipin.com/job_detail/current.html","title":"嵌入式开发","company":"公司","description":"负责嵌入式系统开发与驱动调试，要求熟悉 C 语言、Linux 和 RTOS。"}]}
""";
Check(PlatformCapture.ExtractCurrentDetail("boss",current)?.Description.Contains("RTOS")==true,"single visible selected detail retains real duties and requirements");
Check(PlatformCapture.ExtractCurrentDetail("boss",current.Replace("\"detailVisible\":true","\"detailVisible\":false")) is null,"a list page without an opened detail cannot save");
Check(PlatformCapture.ExtractCurrentDetail("boss",current.Replace("负责嵌入式系统开发与驱动调试，要求熟悉 C 语言、Linux 和 RTOS。","")) is null,"summary without full description cannot save");
Check(PlatformCapture.ExtractCurrentDetail("boss",current.Replace("\"targetUrl\":\"https://www.zhipin.com/job_detail/current.html\"","\"targetUrl\":\"https://www.zhipin.com/job_detail/other.html\"")) is null,"target identity must match parsed job identity");
var noDescription=current.Replace("负责嵌入式系统开发与驱动调试，要求熟悉 C 语言、Linux 和 RTOS。","");
var cache=new Job {CanonicalUrl="https://www.zhipin.com/job_detail/current.html",Url="https://www.zhipin.com/job_detail/current.html",Title="开发",Description="岗位职责：完成嵌入式设备的软件研发、驱动调试和通信模块实现。"};
Check(PlatformCapture.ExtractCurrentDetail("boss",noDescription,[cache])?.Description==cache.Description,"matching detail response can enrich current DOM only");
cache.CanonicalUrl="https://www.zhipin.com/job_detail/other.html";
Check(PlatformCapture.ExtractCurrentDetail("boss",noDescription,[cache]) is null,"unrelated detail response cannot fill current summary");
Console.WriteLine($"PASS: {checks} platform capture checks");
