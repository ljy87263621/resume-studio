package com.resumestudio;

import java.net.*;
import java.net.http.*;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;

/** Provider errors remain visible; empty or blocked feeds are never replaced with fabricated jobs. */
public final class Jobs {
    private static final class Network {
        private static final HttpClient HTTP=HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).followRedirects(HttpClient.Redirect.NEVER).build();
    }
    private record Cached(Instant time,Map<String,Object> result) {}
    private final Map<String,Cached> cache=new ConcurrentHashMap<>();
    private Map<String,Object> fetch(String url) throws Exception {
        HttpRequest req=HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(18)).header("User-Agent","ResumeStudio/0.1").header("Accept","application/json").GET().build();
        var response=Network.HTTP.send(req,HttpResponse.BodyHandlers.ofInputStream());
        try(var body=response.body()) {
            if(response.statusCode()!=200) throw new IllegalStateException("HTTP "+response.statusCode());
            byte[] bytes=body.readNBytes(4_000_001); if(bytes.length>4_000_000) throw new IllegalStateException("Feed too large");
            return Json.object(Json.parse(new String(bytes,java.nio.charset.StandardCharsets.UTF_8)));
        }
    }
    public synchronized Map<String,Object> search(String provider,String keyword,String city,int page) {
        if (!Set.of("tencent","arbeitnow").contains(provider)) throw new IllegalArgumentException("Unsupported provider");
        String key=provider+"|"+keyword+"|"+page;
        Cached old=cache.get(key); boolean reused=old!=null&&Duration.between(old.time,Instant.now()).toMinutes()<10;
        Map<String,Object> result;
        if(reused) result=new LinkedHashMap<>(old.result);
        else {
            List<Map<String,Object>> items=new ArrayList<>();
            try {
                int total;
                if(provider.equals("tencent")) {
                    Map<String,Object> root=fetch("https://careers.tencent.com/tencentcareer/api/post/Query?keyword="+URLEncoder.encode(keyword,java.nio.charset.StandardCharsets.UTF_8)+"&pageIndex="+page+"&pageSize=20&language=zh-cn");
                    if(!"200".equals(Json.str(root,"Code"))) throw new IllegalStateException("Provider rejected query");
                    Map<String,Object> data=Json.object(root.get("Data")); total=Integer.parseInt(Json.str(data,"Count"));
                    for(Object o:Json.array(data.get("Posts"))) {
                        var p=Json.object(o); String company=Json.str(p,"ComName");
                        items.add(job("tencent-"+Json.str(p,"PostId"),Json.str(p,"RecruitPostName"),company.isBlank()?"腾讯":company,Json.str(p,"LocationName"),Json.str(p,"Responsibility"),"https://careers.tencent.com/jobdesc.html?postId="+Json.str(p,"PostId"),"腾讯招聘",Json.str(p,"LastUpdateTime"),Json.str(p,"RequireWorkYearsName")));
                    }
                } else {
                    var root=fetch("https://www.arbeitnow.com/api/job-board-api?page="+page);
                    for(Object o:Json.array(root.get("data"))) {
                        var p=Json.object(o); String description=Json.str(p,"description").replaceAll("<[^>]+>"," ").replace("&amp;","&");
                        if(!(Json.str(p,"title")+" "+description+" "+p.get("tags")).toLowerCase(Locale.ROOT).contains(keyword.toLowerCase(Locale.ROOT))) continue;
                        items.add(job("arbeitnow-"+Integer.toUnsignedString(Json.str(p,"slug").hashCode()),Json.str(p,"title"),Json.str(p,"company_name"),Json.str(p,"location"),description,Json.str(p,"url"),"Arbeitnow",Instant.ofEpochSecond(Long.parseLong(Json.str(p,"created_at"))).toString(),Boolean.TRUE.equals(p.get("remote"))?"远程 / 欧洲":"欧洲地区"));
                    }
                    total=items.size();
                }
                result=new LinkedHashMap<>(Map.of("items",items,"total",total,"status","ok","message",provider.equals("tencent")?"腾讯招聘 · 城市筛选作用于当前页，薪资以原站为准":"Arbeitnow · 欧洲岗位，关键词筛选作用于当前页","fetchedAt",Instant.now().toString(),"page",page));
                cache.put(key,new Cached(Instant.now(),result));
                if(cache.size()>100) cache.entrySet().removeIf(e->Duration.between(e.getValue().time,Instant.now()).toMinutes()>=10);
            } catch(Exception e) {
                if(old!=null) { result=new LinkedHashMap<>(old.result); result.put("status","stale"); result.put("message","网络请求失败，展示上次缓存："+e.getMessage()); }
                else result=new LinkedHashMap<>(Map.of("items",items,"total",0,"status","error","message","招聘源暂不可用："+e.getMessage()+"。可使用下方招聘平台入口。","fetchedAt","","page",page));
            }
        }
        var filtered=Json.array(result.get("items")).stream().map(Json::object).filter(j->Json.str(j,"location").toLowerCase(Locale.ROOT).contains(city.toLowerCase(Locale.ROOT))).toList();
        result.put("items",filtered); result.put("cached",reused); result.put("provider",provider); return result;
    }
    private static Map<String,Object> job(String id,String title,String company,String location,String description,String url,String source,String published,String experience) {
        return Map.of("id",id,"title",title,"company",company,"location",location,"description",description,"url",url,"source",source,"publishedAt",published,"experience",experience,"salary","薪资未公开");
    }
}
