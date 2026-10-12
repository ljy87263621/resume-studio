package com.resumestudio;

import java.net.URI;
import java.util.Map;

public final class CompanyJobsTest {
    public static void main(String[] args) {
        String html="<script type='application/ld+json'>{\"@graph\":[{\"@type\":\"JobPosting\",\"title\":\"嵌入式开发工程师\",\"description\":\"<p>C / Linux</p>\",\"url\":\"/jobs/embedded-123\",\"jobLocation\":{\"address\":{\"addressLocality\":\"深圳\"}}}]}</script>";
        var result=CompanyJobs.extractJsonLd(html,URI.create("https://careers.example.com/"),Map.of("id","fixture","name","测试公司"));
        if(result.size()!=1||!Json.str(result.get(0),"url").equals("https://careers.example.com/jobs/embedded-123")||!Json.str(result.get(0),"location").equals("深圳")) throw new AssertionError("JSON-LD extraction failed");
        if(!CompanyJobs.extractJsonLd("<h1>招聘首页</h1>",URI.create("https://careers.example.com/"),Map.of("id","fixture","name","测试公司")).isEmpty()) throw new AssertionError("Homepage became a fictitious job");
        if(CompanyJobs.robotsAllowed("User-agent: *\nDisallow: /private", "/private/job")) throw new AssertionError("robots path ignored");
        if(!CompanyJobs.robotsAllowed("User-agent: *\nDisallow: /\nAllow: /careers", "/careers/job")) throw new AssertionError("robots longest rule failed");
        if(CompanyJobs.robotsAllowed("User-agent: *\nDisallow: /*?*", "/job?query=x")) throw new AssertionError("robots wildcard ignored");
        System.out.println("PASS: structured job extraction, relative URLs, homepage exclusion, robots rules");
    }
}
