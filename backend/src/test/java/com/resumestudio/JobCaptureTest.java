package com.resumestudio;

import java.net.URI;
import java.util.*;

/** Offline regression cases for original URLs, real structured fields, and unavailable pages. */
public final class JobCaptureTest {
    private static int checks;
    private static void check(boolean condition,String name) { if(!condition) throw new AssertionError(name); checks++; }
    public static void main(String[] args) {
        String url="https://www.zhipin.com/job_detail/example.html?securityId=keep-this&ka=share";
        var preview=JobCapture.preview(Map.of("text",url)); var job=Json.object(preview.get("job"));
        check(Json.str(job,"url").equals(url),"Original access parameters remain in the browser link");
        check(Json.str(job,"canonicalUrl").equals("https://www.zhipin.com/job_detail/example.html"),"Dedup URL excludes tracking/access parameters");
        var second=Json.object(JobCapture.preview(Map.of("text","https://www.zhipin.com/job_detail/example.html?securityId=new&ka=other")).get("job"));
        check(Json.str(job,"id").equals(Json.str(second,"id")),"Changing access parameters does not duplicate a job");
        var markdown=Json.object(JobCapture.preview(Map.of("text","[职位]("+url+")")).get("job"));
        check(Json.str(markdown,"url").equals(url),"Markdown link paste is recognized");
        String html="""
          <script type='application/ld+json'>{"@graph":[{"@type":"JobPosting","title":"嵌入式工程师",
          "hiringOrganization":{"name":"示例公司"},"jobLocation":[{"address":{"addressLocality":"深圳","addressRegion":"广东"}}],
          "description":"<p>开发固件</p><ul><li>掌握 C/C++</li></ul>","baseSalary":{"currency":"CNY","value":{"minValue":15000,"maxValue":25000,"unitText":"MONTH"}}}]}</script>
          """;
        var fields=JobCapture.extractHtml(html,URI.create(url));
        check(Json.str(fields,"title").equals("嵌入式工程师")&&Json.str(fields,"company").equals("示例公司"),"JSON-LD role and hiring organization");
        check(Json.str(fields,"location").contains("深圳")&&Json.str(fields,"salary").contains("15000–25000"),"Structured location and salary");
        check(Json.str(fields,"description").contains("掌握 C/C++")&&!Json.str(fields,"description").contains("<p>"),"HTML descriptions become readable text");
        check(JobCapture.extractHtml("<title>请稍候 - BOSS直聘</title><h1>安全验证</h1>",URI.create(url)).isEmpty(),"Verification page is never presented as a job");
        check(JobCapture.extractHtml("<title>职位招聘</title><meta name='description' content='加入我们的团队'>",URI.create("https://example.com/careers")).isEmpty(),"Recruitment index is not mistaken for a job detail");
        var meta=JobCapture.extractHtml("<meta content='【Java工程师招聘】_示例公司招聘-BOSS直聘' property='og:title'><meta name='description' content='岗位要求：Java'>",URI.create(url));
        check(Json.str(meta,"title").equals("Java工程师")&&Json.str(meta,"company").equals("示例公司"),"BOSS metadata extraction supports attribute order");
        var pasted=Json.object(JobCapture.preview(Map.of("text","职位名称：芯片验证工程师\n公司：示例公司\n工作地点：上海\n薪资：20–30K\n"+url)).get("job"));
        check(Json.str(pasted,"company").equals("示例公司")&&Json.str(pasted,"location").equals("上海"),"Pasted labeled text fills fields");
        var browser=Json.object(JobCapture.preview(Map.of("text",url,"visibleFields",Map.of("title","芯片验证工程师","company","示例公司","description","可见职责"))).get("job"));
        check(Json.str(browser,"title").equals("芯片验证工程师")&&Json.str(browser,"description").equals("可见职责"),"Visible browser fields are mapped without losing the original URL");
        try { JobCapture.canonical("https://127.0.0.1/job/1"); throw new AssertionError("Local address accepted"); } catch(IllegalArgumentException expected) { checks++; }
        System.out.println("PASS: "+checks+" job detail capture checks");
    }
}
