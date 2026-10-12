package com.resumestudio;

import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.util.*;
import java.util.regex.*;

/** Local import of user-copied links/text. Does not scrape authenticated platforms or sync their account favorites. */
public final class JobCapture {
    public static Map<String,Object> preview(Map<String,Object> request) {
        String text=Json.str(request,"text").replace("\r\n","\n").replace('\r','\n').trim();
        if(text.length()>50000) throw new IllegalArgumentException("复制内容过长");
        Matcher match=Pattern.compile("https://[^\\s<>\"']+").matcher(text);
        if(!match.find()) throw new IllegalArgumentException("请粘贴 HTTPS 职位详情链接，可同时粘贴职位名称、公司和要求。");
        String url=canonical(match.group().replaceAll("[，。；、）)]+$",""));
        String source=platform(URI.create(url).getHost());
        String title=Arrays.stream(text.split("\\r?\\n")).map(String::trim).filter(s->!s.isEmpty()&&!s.contains("https://")).findFirst().orElse(source+"职位 · 待补充");
        if(title.length()>180) title=title.substring(0,180);
        String id;
        try { id="external-"+HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(url.getBytes(StandardCharsets.UTF_8))).substring(0,32); }
        catch(NoSuchAlgorithmException e) { throw new IllegalStateException(e); }
        var job=new LinkedHashMap<String,Object>(); job.put("id",id); job.put("url",url); job.put("source",source); job.put("title",title); job.put("company",""); job.put("location",""); job.put("experience",""); job.put("salary","待补充"); job.put("description",text.equals(match.group())?"":text); job.put("publishedAt",""); job.put("notes",""); job.put("infoStatus","用户粘贴，待核对");
        return Map.of("job",job,"message","已识别 "+source+"链接，收藏前可核对 / 补充职位信息。只保存到拾简，不会同步平台账号收藏。");
    }
    public static String canonical(String text) {
        URI uri=URI.create(text.trim()); String host=uri.getHost();
        if(!"https".equals(uri.getScheme())||host==null||uri.getUserInfo()!=null||(uri.getPort()!=-1&&uri.getPort()!=443)) throw new IllegalArgumentException("职位链接必须是 HTTPS 公共网页");
        host=host.toLowerCase(Locale.ROOT);
        if(host.equals("localhost")||!host.contains(".")||host.matches("[0-9.:]+")||host.endsWith(".local")) throw new IllegalArgumentException("请选择招聘平台或企业的公共职位链接");
        String path=uri.getRawPath(); if(path==null||path.isEmpty()||path.equals("/")) throw new IllegalArgumentException("请使用职位详情链接，而不是网站首页");
        if(host.endsWith("zhipin.com")&&!path.startsWith("/job_detail/")) throw new IllegalArgumentException("BOSS 请复制 /job_detail/ 开头的具体职位链接");
        var queries=new ArrayList<String>();
        if(uri.getRawQuery()!=null) for(String q:uri.getRawQuery().split("&")) {
            String key=q.split("=",2)[0].toLowerCase(Locale.ROOT);
            if(!key.startsWith("utm_")&&!Set.of("ka","sid","source","from","track","tracking","lid","securityid").contains(key)) queries.add(q);
        }
        return "https://"+host+path+(queries.isEmpty()?"":"?"+String.join("&",queries));
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
