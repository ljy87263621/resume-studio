package com.resumestudio;

import java.net.*;
import java.net.http.*;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.regex.*;
import java.nio.charset.StandardCharsets;
import java.security.*;

/** Employer public API adapters plus bounded JSON-LD extraction. Dynamic/login-only sites expose an explicit unavailable status. */
public final class CompanyJobs {
    private final CompanySources registry; private final Jobs legacy=new Jobs();
    private static final class Network {
        private static final HttpClient HTTP=HttpClient.newBuilder().version(HttpClient.Version.HTTP_1_1).connectTimeout(Duration.ofSeconds(6)).followRedirects(HttpClient.Redirect.NEVER).build();
    }
    private final ExecutorService workers=Executors.newFixedThreadPool(4,r->{Thread t=new Thread(r,"employer-source");t.setDaemon(true);return t;});
    private record Cached(Instant time,Map<String,Object> value) {}
    private final Map<String,Cached> cache=new ConcurrentHashMap<>();
    private final Map<String,Map<String,Object>> states=new ConcurrentHashMap<>();
    public CompanyJobs(Store store) { registry=new CompanySources(store); }
    public List<Map<String,Object>> sources() throws Exception {
        var list=registry.all();
        for(var source:list) { var state=states.get(Json.str(source,"id")); if(state!=null) source.putAll(state); else { source.put("status","未查询"); source.put("message",Json.str(source,"adapter").equals("jsonld")?"通用官网检测，尚未验证能否提取职位":"公共招聘系统接口，尚未查询"); } }
        return list;
    }
    public Map<String,Object> search(String provider,String keyword,String city,int page) throws Exception {
        var configured=registry.all(); boolean all=provider.equals("all");
        var selected=configured.stream().filter(s->all?Boolean.TRUE.equals(s.get("enabled")):Json.str(s,"id").equals(provider)).sorted(Comparator.comparing(s->Json.str(s,"adapter").equals("jsonld"))).limit(50).toList();
        if(selected.isEmpty()) throw new IllegalArgumentException("没有可查询的公司来源");
        var futures=selected.stream().map(s->CompletableFuture.supplyAsync(()->query(s,keyword,city,page),workers).completeOnTimeout(result(List.of(),0,"error","本轮官网查询超过30秒；可单独选择该公司重试。",page),30,TimeUnit.SECONDS)).toList();
        var jobs=new LinkedHashMap<String,Map<String,Object>>(); var statuses=new ArrayList<Map<String,Object>>(); int total=0; boolean cached=true;
        for(int i=0;i<futures.size();i++) {
            var result=futures.get(i).join(); var source=selected.get(i); int count=Json.array(result.get("items")).size();
            var state=new LinkedHashMap<String,Object>(Map.of("status",result.get("status"),"message",result.get("message"),"lastQueryAt",Instant.now().toString(),"lastCount",count)); states.put(Json.str(source,"id"),state);
            var display=new LinkedHashMap<>(state); display.put("id",source.get("id")); display.put("name",source.get("name")); display.put("url",source.get("url")); statuses.add(display);
            for(Object value:Json.array(result.get("items"))) { var job=Json.object(value); jobs.put(Json.str(job,"url"),job); }
            total+=Integer.parseInt(Json.str(result,"total")); cached&=Boolean.TRUE.equals(result.get("cached"));
        }
        if(!all) { var result=new LinkedHashMap<>(futures.get(0).join()); result.put("sources",statuses); return result; }
        long ok=statuses.stream().filter(s->Json.str(s,"status").equals("ok")).count();
        return Map.of("items",new ArrayList<>(jobs.values()),"sources",statuses,"total",total,"page",page,"cached",cached,"status",ok>0?"ok":"error","fetchedAt",Instant.now().toString(),"message","本轮查询 "+selected.size()+" 个启用来源，"+ok+" 个成功查询。每页合并各来源20条，最多汇总50个来源；失败官网可在来源面板打开。仅展示真实职位，城市筛选作用于当前结果。");
    }
    private Map<String,Object> query(Map<String,Object> source,String keyword,String city,int page) {
        String adapter=Json.str(source,"adapter"),key=Json.str(source,"id")+"|"+Json.write(source)+"|"+keyword+"|"+city+"|"+page;
        var previous=cache.get(key);
        if(previous!=null&&Duration.between(previous.time,Instant.now()).toMinutes()<10) { var r=new LinkedHashMap<>(previous.value); r.put("cached",true); return r; }
        try {
            Map<String,Object> result;
            if(adapter.equals("tencent")||adapter.equals("arbeitnow")) result=legacy.search(adapter,keyword,city,page);
            else {
                var items=new ArrayList<Map<String,Object>>(); String slug=Json.str(source,"slug"),name=Json.str(source,"name"); int total=0;
                switch(adapter) {
                    case "greenhouse" -> {
                        var data=Json.object(json("https://boards-api.greenhouse.io/v1/boards/"+slug+"/jobs"));
                        for(Object v:Json.array(data.get("jobs"))) { var p=Json.object(v); items.add(job(source,Json.str(p,"title"),name,nested(p,"location","name"),"企业官网公开职位列表；完整职责与任职要求请打开官网详情。",Json.str(p,"absolute_url"),Json.str(p,"updated_at"),"", "薪资以官网为准")); }
                    }
                    case "lever" -> {
                        for(Object v:Json.array(json("https://api.lever.co/v0/postings/"+slug+"?mode=json&limit=20&skip="+((page-1)*20)))) { var p=Json.object(v); String description=Json.str(p,"descriptionPlain"); if(description.isBlank()) description=plain(Json.str(p,"description"));
                            if(p.get("lists") instanceof List<?> lists) for(Object list:lists) { var section=Json.object(list); description+="\n"+Json.str(section,"text")+"\n"+plain(Json.str(section,"content")); }
                            String date=Json.str(p,"createdAt"); try { if(!date.isBlank()) date=Instant.ofEpochMilli(Long.parseLong(date)).toString(); } catch(NumberFormatException ignored) { }
                            items.add(job(source,Json.str(p,"text"),name,nested(p,"categories","location"),description,Json.str(p,"hostedUrl"),date,nested(p,"categories","commitment"),"薪资以官网为准")); }
                    }
                    case "ashby" -> {
                        var data=Json.object(json("https://api.ashbyhq.com/posting-api/job-board/"+slug+"?includeCompensation=true"));
                        for(Object v:Json.array(data.get("jobs"))) { var p=Json.object(v); if(Boolean.FALSE.equals(p.get("isListed"))) continue; String salary=nested(p,"compensation","scrapeableCompensationSalarySummary");
                            items.add(job(source,Json.str(p,"title"),name,Json.str(p,"location"),Json.str(p,"descriptionPlain"),Json.str(p,"jobUrl"),Json.str(p,"publishedAt"),Json.str(p,"employmentType"),salary.isBlank()?"薪资未公开":salary)); }
                    }
                    case "smartrecruiters" -> {
                        var data=Json.object(json("https://api.smartrecruiters.com/v1/companies/"+slug+"/postings?limit=20&offset="+((page-1)*20)+"&q="+encode(english(keyword)))); total=Integer.parseInt(Json.str(data,"totalFound"));
                        for(Object v:Json.array(data.get("content"))) { var p=Json.object(v); items.add(job(source,Json.str(p,"name"),name,nested(p,"location","fullLocation"),"官网公共列表未包含完整岗位描述，请打开原始招聘页面查看职责与任职要求。", "https://jobs.smartrecruiters.com/"+slug+"/"+Json.str(p,"id"),Json.str(p,"releasedDate"),nested(p,"experienceLevel","label"),"薪资以官网为准")); }
                    }
                    case "workday" -> {
                        URI site=URI.create(Json.str(source,"url")); String tenant=site.getHost().split("\\.")[0],board=site.getPath().replaceAll("^/|/$","");
                        var data=Json.object(Json.parse(request(URI.create("https://"+site.getHost()+"/wday/cxs/"+tenant+"/"+board+"/jobs"),Json.write(Map.of("appliedFacets",Map.of(),"limit",20,"offset",(page-1)*20,"searchText",english(keyword)))))); total=Integer.parseInt(Json.str(data,"total"));
                        for(Object v:Json.array(data.get("jobPostings"))) { var p=Json.object(v); items.add(job(source,Json.str(p,"title"),name,Json.str(p,"locationsText"),"企业官网公共职位列表；完整职责与任职要求请打开官网详情。",site.toString().replaceAll("/$","")+Json.str(p,"externalPath"),Json.str(p,"postedOn"),"","薪资以官网为准")); }
                    }
                    case "jsonld" -> {
                        URI url=URI.create(Json.str(source,"url")); String html=webpage(url); items.addAll(extractJsonLd(html,url,source));
                        if(items.isEmpty()) {
                            var links=Pattern.compile("(?is)<a[^>]+href=[\"']([^\"']+)[\"'][^>]*>").matcher(html); int visited=0; var seen=new HashSet<String>();
                            while(links.find()&&visited<2) { URI child;
                                try { child=url.resolve(links.group(1).replace("&amp;","&")); } catch(IllegalArgumentException e) { continue; }
                                if(!Objects.equals(child.getHost(),url.getHost())||!"https".equals(child.getScheme())||!child.getPath().matches("(?i).*(job|position|career|vacanc|recruit).*" )||!seen.add(child.toString())||child.equals(url)) continue;
                                visited++; try { items.addAll(extractJsonLd(webpage(child),child,source)); } catch(Exception ignored) { }
                            }
                        }
                        if(items.isEmpty()) return result(List.of(),0,"unsupported","该官网未提取到公开 JobPosting；可能是动态招聘系统，需要专用适配。请打开官网浏览，或配置其 ATS 公司标识。",page);
                    }
                    default -> throw new IllegalArgumentException("未知来源类型");
                }
                var filtered=items.stream().filter(j->matches(Json.str(j,"title")+" "+Json.str(j,"description"),keyword)&&matches(Json.str(j,"location"),city)).toList();
                if(!Set.of("smartrecruiters","workday","lever").contains(adapter)) { total=filtered.size(); filtered=filtered.stream().skip((long)(page-1)*20).limit(20).toList(); }
                if(adapter.equals("lever")) total=filtered.size();
                result=result(filtered,total,"ok",name+" · "+adapter+" 公共职位源"+(Set.of("smartrecruiters","workday","lever").contains(adapter)?"，城市筛选作用于当前页；Lever 关键词与数量也仅指当前页":"，本次完整返回源内筛选 / 翻页"),page);
            }
            if(Json.str(result,"status").equals("ok")) { cache.put(key,new Cached(Instant.now(),result)); if(cache.size()>150) cache.entrySet().stream().min(Comparator.comparing(e->e.getValue().time)).ifPresent(e->cache.remove(e.getKey())); }
            return result;
        } catch(Exception e) { return result(List.of(),0,"error",Json.str(source,"name")+"："+Objects.toString(e.getMessage(),e.getClass().getSimpleName()),page); }
    }
    private static Map<String,Object> result(List<?> jobs,int total,String status,String message,int page) { return new LinkedHashMap<>(Map.of("items",jobs,"total",total,"status",status,"message",message,"fetchedAt",Instant.now().toString(),"cached",false,"page",page)); }
    private Object json(String url) throws Exception { return Json.parse(get(URI.create(url))); }
    private String get(URI url) throws Exception { return request(url,null); }
    private String request(URI url,String payload) throws Exception {
        checkPublic(url);
        var builder=HttpRequest.newBuilder(url).timeout(Duration.ofSeconds(18)).header("User-Agent","ResumeStudio/0.3 (+public-career-reader)").header("Accept","application/json,text/html;q=0.9");
        if(payload==null) builder.GET(); else builder.header("Content-Type","application/json").POST(HttpRequest.BodyPublishers.ofString(payload));
        var req=builder.build();
        var pending=Network.HTTP.sendAsync(req,info->new LimitedBody());
        try { var response=pending.get(18,TimeUnit.SECONDS); if(response.statusCode()!=200) throw new IllegalStateException("HTTP "+response.statusCode()+"（官网可在浏览器中打开）"); return new String(response.body(),StandardCharsets.UTF_8); }
        catch(TimeoutException e) { pending.cancel(true); throw new IllegalStateException("官网请求超时"); }
    }
    private static final class LimitedBody implements HttpResponse.BodySubscriber<byte[]> {
        private final HttpResponse.BodySubscriber<byte[]> delegate=HttpResponse.BodySubscribers.ofByteArray();
        private java.util.concurrent.Flow.Subscription subscription; private long count;
        public CompletionStage<byte[]> getBody() { return delegate.getBody(); }
        public void onSubscribe(java.util.concurrent.Flow.Subscription s) { subscription=s; delegate.onSubscribe(s); }
        public void onNext(List<java.nio.ByteBuffer> buffers) { for(var b:buffers) count+=b.remaining(); if(count>16_000_000) { subscription.cancel(); delegate.onError(new IllegalStateException("官网数据超过16MB")); } else delegate.onNext(buffers); }
        public void onError(Throwable error) { delegate.onError(error); }
        public void onComplete() { delegate.onComplete(); }
    }
    private String webpage(URI url) throws Exception {
        String robots;
        try { robots=get(URI.create("https://"+url.getHost()+"/robots.txt")); } catch(Exception e) { robots=""; }
        if(!robotsAllowed(robots,url.getRawPath()+(url.getRawQuery()==null?"":"?"+url.getRawQuery()))) throw new IllegalStateException("官网 robots 限制当前路径；可打开官网浏览");
        return get(url);
    }
    private static void checkPublic(URI uri) throws Exception {
        if(!"https".equals(uri.getScheme())||uri.getHost()==null||uri.getUserInfo()!=null||(uri.getPort()!=-1&&uri.getPort()!=443)) throw new IllegalArgumentException("仅请求公共 HTTPS 官网");
        for(var address:InetAddress.getAllByName(uri.getHost())) if(address.isAnyLocalAddress()||address.isLoopbackAddress()||address.isLinkLocalAddress()||address.isSiteLocalAddress()||address.isMulticastAddress()||(address instanceof Inet6Address&&((address.getAddress()[0]&0xfe)==0xfc))) throw new IllegalArgumentException("不请求本机或私有网络地址");
    }
    static boolean robotsAllowed(String text,String path) {
        boolean group=false,allowed=true; int longest=-1;
        for(String line:text.split("\\r?\\n")) { line=line.split("#",2)[0].trim(); var kv=line.split(":",2); if(kv.length<2) continue;
            String key=kv[0].trim().toLowerCase(Locale.ROOT),value=kv[1].trim();
            if(key.equals("user-agent")) { group=value.equals("*"); continue; }
            if(!group||value.isEmpty()||(!key.equals("allow")&&!key.equals("disallow"))) continue;
            String regex="^"+Pattern.quote(value).replace("*","\\E.*\\Q"); if(value.endsWith("$")) regex=regex.substring(0,regex.length()-3)+"\\E$";
            if(Pattern.compile(regex).matcher(path).find()&&value.length()>=longest) { longest=value.length(); allowed=key.equals("allow"); }
        }
        return allowed;
    }
    static List<Map<String,Object>> extractJsonLd(String html,URI base,Map<String,Object> source) {
        var jobs=new ArrayList<Map<String,Object>>(); var matcher=Pattern.compile("(?is)<script[^>]*type\\s*=\\s*[\"']application/ld\\+json[\"'][^>]*>(.*?)</script>").matcher(html);
        while(matcher.find()) try { collect(Json.parse(matcher.group(1).trim()),base,source,jobs); } catch(IllegalArgumentException ignored) { }
        return jobs;
    }
    private static void collect(Object value,URI base,Map<String,Object> source,List<Map<String,Object>> out) {
        if(value instanceof List<?> list) { list.forEach(v->collect(v,base,source,out)); return; }
        if(!(value instanceof Map<?,?>)) return; var p=Json.object(value);
        if(p.containsKey("@graph")) collect(p.get("@graph"),base,source,out);
        if(!Json.str(p,"@type").contains("JobPosting")) return;
        String until=Json.str(p,"validThrough"); try { if(!until.isBlank()&&Instant.parse(until).isBefore(Instant.now())) return; } catch(java.time.format.DateTimeParseException ignored) { }
        String title=Json.str(p,"title"),url=Json.str(p,"url"); if(title.isBlank()) return; if(url.isBlank()) url=base.toString(); else url=base.resolve(url).toString();
        String location=""; Object l=p.get("jobLocation"); if(l instanceof List<?> list&&!list.isEmpty()) l=list.get(0);
        if(l instanceof Map<?,?>) { var lm=Json.object(l); location=nested(lm,"address","addressLocality")+" "+nested(lm,"address","addressRegion"); }
        out.add(job(source,title,Json.str(source,"name"),location.trim(),plain(Json.str(p,"description")),url,Json.str(p,"datePosted"),Json.str(p,"employmentType"),"薪资以官网为准"));
    }
    private static Map<String,Object> job(Map<String,Object> source,String title,String company,String location,String description,String url,String date,String experience,String salary) {
        var j=new LinkedHashMap<String,Object>(); String id;
        try { id="official-"+HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest((Json.str(source,"id")+"|"+url).getBytes(StandardCharsets.UTF_8))).substring(0,32); } catch(NoSuchAlgorithmException e) { throw new IllegalStateException(e); }
        j.put("id",id); j.put("title",title); j.put("company",company); j.put("location",location); j.put("description",description.length()>30000?description.substring(0,30000):description); j.put("url",url); j.put("publishedAt",date); j.put("experience",experience); j.put("salary",salary); j.put("source",Json.str(source,"name")+"官方招聘"); j.put("infoStatus","公开官网职位"); return j;
    }
    private static String nested(Map<String,Object> p,String a,String b) { return p.get(a) instanceof Map<?,?>?Json.str(Json.object(p.get(a)),b):""; }
    private static String plain(String s) { return s.replaceAll("(?is)<(script|style)[^>]*>.*?</\\1>","").replaceAll("(?i)<br\\s*/?>|</p>|</li>","\n").replaceAll("<[^>]*>","").replace("&nbsp;"," ").replace("&amp;","&").replace("&lt;","<").replace("&gt;",">").replace("&quot;","\"").trim(); }
    private static String encode(String s) { return URLEncoder.encode(s,StandardCharsets.UTF_8); }
    private static String english(String s) { return Map.of("嵌入式","embedded","前端","frontend","后端","backend","实习","intern","深圳","Shenzhen","北京","Beijing","上海","Shanghai").getOrDefault(s,s); }
    private static boolean matches(String text,String query) { if(query.isBlank()) return true; return text.toLowerCase(Locale.ROOT).contains(query.toLowerCase(Locale.ROOT))||text.toLowerCase(Locale.ROOT).contains(english(query).toLowerCase(Locale.ROOT)); }
}
