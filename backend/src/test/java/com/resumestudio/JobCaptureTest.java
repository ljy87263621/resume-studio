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
        String employer="""
            <title>嵌入式软件开发工程师-加入芯德 - 芯德科技</title><meta name="description" content="">
            <header><h1>导航标题</h1><nav>产品中心</nav></header>
            <section><h1>嵌入式软件开发工程师（2027届）</h1></section>
            <main><div class="entry-content"><div><div><h2><strong>嵌入式软件开发工程师</strong>（2027届）</h2></div></div>
            <h3><strong>岗位职责：</strong></h3><p>根据项目需求进行软件开发，编写设计文档、代码并调试。</p>
            <h3><strong>任职资格：</strong></h3><p>熟悉嵌入式 Linux 系统，C 或 C++语言编程。</p>
            <h3>工作地点：</h3><p>广州</p><h3>投递方式：</h3><p>发送简历至招聘邮箱。</p>
            <div hidden>内部隐藏测试数据</div><script>var tracking='不可保存脚本';</script></div></main>
            <footer>产品中心 版权所有</footer><form>立刻咨询 企业组织</form>
            """;
        var employerFields=JobCapture.extractHtml(employer,URI.create("https://www.vsol.com.cn/join-us/campus-recruitment/embedded-software-engineer-1"));
        check(Json.str(employerFields,"title").equals("嵌入式软件开发工程师（2027届）")&&Json.str(employerFields,"company").equals("芯德科技"),"Employer title and site company are extracted without JSON-LD or description metadata");
        check(Json.str(employerFields,"location").equals("广州"),"Heading followed by paragraph supplies location");
        String employerDescription=Json.str(employerFields,"description");
        check(employerDescription.contains("岗位职责")&&employerDescription.contains("任职资格")&&employerDescription.contains("C 或 C++")&&employerDescription.contains("投递方式"),"Nested employer article preserves duties requirements and application instructions");
        check(!employerDescription.contains("产品中心")&&!employerDescription.contains("不可保存脚本")&&!employerDescription.contains("隐藏测试")&&!employerDescription.contains("立刻咨询"),"Navigation footer forms scripts and hidden blocks stay out of job content");
        check(Json.str(employerFields,"salary").isBlank(),"Unpublished salary is never invented");
        String generic="<title>职位信息 | 示例科技</title><main><article><header><h1>测试工程师</h1></header><h2>岗位职责</h2><p>调试固件并验证产品需求，记录测试结果。</p><h2>任职要求</h2><p>了解 C 语言、通信协议与测试方法。</p><h3>工作地点</h3><p>上海</p></article></main>";
        var genericFields=JobCapture.extractHtml(generic,URI.create("https://example.com/job/test"));
        check(Json.str(genericFields,"title").equals("测试工程师")&&Json.str(genericFields,"location").equals("上海"),"Generic semantic article and article header work without a site-specific adapter");
        check(JobCapture.extractHtml(generic.replace("测试工程师","2027校园招聘"),URI.create("https://example.com/careers")).isEmpty(),"Recruitment index with duty keywords is still rejected");
        check(JobCapture.extractHtml(generic.replace("测试工程师","安全验证"),URI.create("https://example.com/job/test")).isEmpty(),"Challenge page cannot become a generic employer job");
        check(JobPageText.decode("C&#43;&#43; &amp; Linux &#x4E2D;文").equals("C++ & Linux 中文"),"Numeric and named HTML entities decode correctly");
        try { JobCapture.canonical("https://127.0.0.1/job/1"); throw new AssertionError("Local address accepted"); } catch(IllegalArgumentException expected) { checks++; }
        System.out.println("PASS: "+checks+" job detail capture checks");
    }
}
