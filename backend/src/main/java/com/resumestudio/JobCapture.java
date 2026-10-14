package com.resumestudio;

import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.util.*;
import java.util.regex.*;

/** User-directed public detail extraction; canonical IDs are separate from the original browser link. */
public final class JobCapture {
    public static Map<String,Object> preview(Map<String,Object> request) {
        String text=Json.str(request,"text").replace("\r\n","\n").replace('\r','\n').trim();
        if(text.length()>50000) throw new IllegalArgumentException("复制内容过长");
        Matcher match=Pattern.compile("https://[^\\s<>\"'\\[\\]]+").matcher(text);
        if(!match.find()) throw new IllegalArgumentException("请粘贴 HTTPS 职位详情链接，可同时粘贴职位名称、公司和要求。");
        String original=match.group().replaceAll("[，。；、）)]+$","");
        String url=canonical(original);
        String source=platform(URI.create(url).getHost());
        String title=Arrays.stream(text.split("\\r?\\n")).map(String::trim).filter(s->!s.isEmpty()&&!s.contains("https://")).findFirst().orElse(source+"职位 · 待补充");
        if(title.length()>180) title=title.substring(0,180);
        String id;
        try { id="external-"+HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(url.getBytes(StandardCharsets.UTF_8))).substring(0,32); }
        catch(NoSuchAlgorithmException e) { throw new IllegalStateException(e); }
        var job=new LinkedHashMap<String,Object>(); job.put("id",id); job.put("url",original); job.put("canonicalUrl",url); job.put("source",source); job.put("title",title); job.put("company",""); job.put("location",""); job.put("experience",""); job.put("salary",""); job.put("description",text.equals(match.group())?"":text); job.put("publishedAt",""); job.put("notes",""); job.put("infoStatus","用户粘贴，待核对");
        labels(text,job);
        String status="link_only",message="已保留原始链接。收藏前可核对 / 补充职位信息。";
        if(Boolean.TRUE.equals(request.get("resolve"))) {
            try {
                var fields=extractHtml(CompanyJobs.webpage(URI.create(original)),URI.create(original));
                if(!fields.isEmpty()) { merge(job,fields); status="parsed"; job.put("infoStatus","公开详情自动解析，待核对"); message="已从公开详情页自动填入可读取的信息，请核对后收藏。"; }
                else { status="unavailable"; message="已读取网页，但未识别出明确的职位正文。可以使用“浏览并读取”加载完整页面，或粘贴详情文本后核对收藏。"; }
            } catch(Exception e) { status="unavailable"; message="未能自动读取详情（"+Objects.toString(e.getMessage(),"网络不可用")+"）。可使用“浏览并读取”，或粘贴详情文本；原始链接已保留。"; }
        }
        if(request.get("visibleFields") instanceof Map<?,?> fields) {
            merge(job,Json.object(fields)); job.put("infoStatus","浏览页面读取，待核对");
            status="browser"; message="已读取当前页面可见的职位信息，请核对后收藏。";
        }
        return Map.of("job",job,"status",status,"message",message);
    }
    public static String canonical(String text) {
        URI uri=URI.create(text.trim()); String host=uri.getHost();
        if(!"https".equals(uri.getScheme())||host==null||uri.getUserInfo()!=null||(uri.getPort()!=-1&&uri.getPort()!=443)) throw new IllegalArgumentException("职位链接必须是 HTTPS 公共网页");
        host=host.toLowerCase(Locale.ROOT);
        if(host.equals("localhost")||!host.contains(".")||host.matches("[0-9.:]+")||host.endsWith(".local")) throw new IllegalArgumentException("请选择招聘平台或企业的公共职位链接");
        String path=uri.getRawPath(); if(path==null||path.isEmpty()||path.equals("/")) throw new IllegalArgumentException("请使用职位详情链接，而不是网站首页");
        if(domain(host,"zhipin.com")&&!path.startsWith("/job_detail/")) throw new IllegalArgumentException("BOSS 请复制 /job_detail/ 开头的具体职位链接");
        var queries=new ArrayList<String>();
        if(uri.getRawQuery()!=null) for(String q:uri.getRawQuery().split("&")) {
            String key=q.split("=",2)[0].toLowerCase(Locale.ROOT);
            if(!key.startsWith("utm_")&&!Set.of("ka","sid","source","from","track","tracking","lid","securityid","channel","pagesource","deliversource").contains(key)) queries.add(q);
        }
        return "https://"+host+path+(queries.isEmpty()?"":"?"+String.join("&",queries));
    }
    private static void merge(Map<String,Object> job,Map<String,Object> fields) {
        for(String key:List.of("title","company","location","salary","description","experience","publishedAt")) {
            String value=Json.str(fields,key).trim(); if(value.isEmpty()) continue;
            int limit=key.equals("description")?30000:key.equals("salary")?100:250;
            job.put(key,value.substring(0,Math.min(limit,value.length())));
        }
    }
    private static void labels(String text,Map<String,Object> job) {
        for(var label:Map.of("title","职位名称|岗位名称","company","公司名称|招聘公司|公司","location","工作地点|城市|地点","salary","薪资范围|薪资|工资","experience","工作经验|经验要求").entrySet()) {
            var m=Pattern.compile("(?m)^(?:"+label.getValue()+")[：:]\\s*(.+)$").matcher(text);
            if(m.find()) job.put(label.getKey(),m.group(1).trim());
        }
    }
    static Map<String,Object> extractHtml(String html,URI base) {
        var scripts=Pattern.compile("(?is)<script[^>]*type\\s*=\\s*[\"']application/ld\\+json[\"'][^>]*>(.*?)</script>").matcher(html);
        while(scripts.find()) {
            try { var found=posting(Json.parse(scripts.group(1).trim())); if(found!=null&&!Json.str(found,"title").isBlank()) return postingFields(found); }
            catch(IllegalArgumentException ignored) { }
        }
        var publicBody=JobPageText.fields(html,base);if(!publicBody.isEmpty())return publicBody;
        var result=new LinkedHashMap<String,Object>();
        String title=meta(html,"og:title"); if(title.isBlank()) title=element(html,"h1"); if(title.isBlank()) title=element(html,"title");
        if(title.isBlank()||title.matches("(?is).*(请稍候|安全验证|人机验证|页面不存在|404|访问受限|登录验证|登录注册|Access denied|Checking your browser|Just a moment|captcha).*")) return result;
        var boss=Pattern.compile("【(.+?)招聘】[_\\-－]*(.+?)招聘[_\\-－]BOSS直聘").matcher(title);
        if(boss.find()) { result.put("title",boss.group(1)); result.put("company",boss.group(2)); }
        else result.put("title",title);
        String description=meta(html,"og:description"); if(description.isBlank()) description=meta(html,"description");
        result.put("description",description);
        // Site-wide headings/metadata are not a job. Generic pages need at least job-specific evidence.
        boolean roleAndDuties=title.matches("(?is).*(engineer|developer|工程师|开发人员).*") && description.matches("(?is).*(responsibilities|requirements|职责|任职|要求).*");
        if(!boss.find(0) && !html.contains("job-sec-text") && !html.contains("job-description") && !html.contains("hiringOrganization") && !roleAndDuties) return Map.of();
        return result;
    }
    private static Map<String,Object> posting(Object value) {
        if(value instanceof List<?> values) { for(Object item:values) { var found=posting(item); if(found!=null) return found; } return null; }
        if(!(value instanceof Map<?,?>)) return null;
        var object=Json.object(value);
        if(Json.str(object,"@type").contains("JobPosting")) return object;
        return object.containsKey("@graph")?posting(object.get("@graph")):null;
    }
    private static Map<String,Object> postingFields(Map<String,Object> p) {
        var result=new LinkedHashMap<String,Object>();
        result.put("title",plain(Json.str(p,"title"))); result.put("description",plain(Json.str(p,"description"))); result.put("publishedAt",Json.str(p,"datePosted"));
        Object company=p.get("hiringOrganization"); result.put("company",company instanceof Map<?,?>?Json.str(Json.object(company),"name"):Objects.toString(company,""));
        Object location=p.get("jobLocation"); var locations=new LinkedHashSet<String>();
        for(Object item:location instanceof List<?> list?list:Collections.singletonList(location)) {
            if(!(item instanceof Map<?,?>)) continue;
            Object address=Json.object(item).get("address");
            if(address instanceof Map<?,?>) {
                var a=Json.object(address); String s=String.join(" ",List.of(Json.str(a,"addressLocality"),Json.str(a,"addressRegion"),Json.str(a,"streetAddress"))).trim(); if(!s.isBlank()) locations.add(s);
            }
        }
        result.put("location",locations.isEmpty()?Json.str(p,"jobLocationType"):String.join(" / ",locations));
        result.put("experience",Json.str(p,"experienceRequirements"));
        if(p.get("baseSalary") instanceof Map<?,?>) {
            var salary=Json.object(p.get("baseSalary")); Object value=salary.get("value"); String amount=Objects.toString(value,"");
            if(value instanceof Map<?,?>) { var v=Json.object(value); String low=Json.str(v,"minValue"),high=Json.str(v,"maxValue"); amount=low.isBlank()?Json.str(v,"value"):low+(high.isBlank()?"":"–"+high); String unit=Json.str(v,"unitText"); if(!unit.isBlank()) amount+=" / "+unit; }
            result.put("salary",(Json.str(salary,"currency")+" "+amount).trim());
        }
        return result;
    }
    private static String element(String html,String tag) {
        var m=Pattern.compile("(?is)<"+tag+"\\b[^>]*>(.*?)</"+tag+">").matcher(html); return m.find()?plain(m.group(1)):"";
    }
    private static String meta(String html,String key) {
        var tags=Pattern.compile("(?is)<meta\\b[^>]*>").matcher(html);
        while(tags.find()) {
            var attrs=new HashMap<String,String>(); var m=Pattern.compile("([\\w:-]+)\\s*=\\s*([\"'])(.*?)\\2",Pattern.DOTALL).matcher(tags.group());
            while(m.find()) attrs.put(m.group(1).toLowerCase(Locale.ROOT),m.group(3));
            if(key.equalsIgnoreCase(attrs.getOrDefault("property",attrs.getOrDefault("name","")))) return plain(attrs.getOrDefault("content",""));
        }
        return "";
    }
    private static String plain(String value) {
        return value.replaceAll("(?is)<(script|style)\\b[^>]*>.*?</\\1>","").replaceAll("(?i)<br\\s*/?>|</p>|</li>|</div>","\n").replaceAll("<[^>]*>","").replace("&nbsp;"," ").replace("&amp;","&").replace("&lt;","<").replace("&gt;",">").replace("&quot;","\"").replace("&#39;","'").trim();
    }
    private static boolean domain(String host,String expected) { return host.equals(expected)||host.endsWith("."+expected); }
    public static String platform(String host) {
        if(domain(host,"zhipin.com")) return "BOSS直聘";
        if(domain(host,"liepin.com")) return "猎聘";
        if(domain(host,"zhaopin.com")) return "智联招聘";
        if(domain(host,"51job.com")) return "前程无忧";
        if(domain(host,"lagou.com")) return "拉勾";
        if(domain(host,"nowcoder.com")) return "牛客";
        if(domain(host,"tencent.com")) return "腾讯招聘";
        return "企业 / 其他招聘页";
    }
}
