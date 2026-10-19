package com.resumestudio;

import com.sun.net.httpserver.*;
import java.net.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

public final class Server {
    private final Path dataDirectory;private final Store store; private volatile CompanyJobs jobs; private final String token;
    private final HttpServer server;
    private final ExecutorService executor=Executors.newFixedThreadPool(6);
    public Server(int port,Path data,String token) throws Exception {
        if(token.length()<16) throw new IllegalArgumentException("Provide a session token of at least 16 characters");
        this.token=token;dataDirectory=data; store=new Store(data);if(!Files.exists(data.resolve("device-name.txt")))Files.writeString(data.resolve("device-name.txt"),System.getenv().getOrDefault("COMPUTERNAME","Windows 设备")); jobs=new CompanyJobs(store);
        server=HttpServer.create(new InetSocketAddress(InetAddress.getByName("127.0.0.1"),port),16);
        server.setExecutor(executor); server.createContext("/api/",this::handle);
    }
    public void start() { server.start(); }
    public int port() { return server.getAddress().getPort(); }
    public void stop() { server.stop(0); executor.shutdownNow(); }
    private void handle(HttpExchange e) {
        try {
            if(!token.equals(e.getRequestHeaders().getFirst("X-Resume-Token"))) { send(e,401,Map.of("error","Unauthorized")); return; }
            String path=e.getRequestURI().getPath().substring(5), method=e.getRequestMethod();
            if(path.equals("health")&&method.equals("GET")) { send(e,200,Map.of("status","ok","version","0.9.3","java",System.getProperty("java.version"))); return; }
            if(path.equals("templates")&&method.equals("GET")) { send(e,200,Templates.all()); return; }
            if((path.equals("resume-import")||path.equals("resume-import/create"))&&method.equals("POST")){
                byte[] bytes=e.getRequestBody().readNBytes(1000001);if(bytes.length>1000000)throw new IllegalArgumentException("简历导入内容过大");
                var request=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8)));
                if(path.endsWith("/create")){var resume=ResumeImport.create(request);validate("resumes",resume);send(e,200,store.save("resumes",resume));}
                else send(e,200,ResumeImport.parse(request));return;
            }
            if(path.equals("job-sources")&&method.equals("GET")) { send(e,200,jobs.sources()); return; }
            if(path.equals("export/docx")&&method.equals("POST")){var request=Json.object(Json.parse(new String(e.getRequestBody().readNBytes(200001),StandardCharsets.UTF_8)));String id=Json.str(request,"id");var resume=store.list("resumes").stream().filter(r->id.equals(Json.str(r,"id"))).findFirst().orElseThrow(()->new IllegalArgumentException("简历不存在"));send(e,200,Map.of("base64",Base64.getEncoder().encodeToString(ResumeDocx.create(resume))));return;}
            if(path.equals("workspace")&&method.equals("POST")){var request=Json.object(Json.parse(new String(e.getRequestBody().readNBytes(200001),StandardCharsets.UTF_8)));send(e,200,WorkspaceTools.call(store,request));return;}
            if(path.equals("backups")&&method.equals("POST")){var request=Json.object(Json.parse(new String(e.getRequestBody().readNBytes(SyncStore.MAX_BYTES*2+1),StandardCharsets.UTF_8)));send(e,200,BackupCatalog.call(dataDirectory,store,request));jobs=new CompanyJobs(store);return;}
            if(path.equals("sync/snapshot")&&method.equals("GET")) { send(e,200,store.syncSnapshot()); return; }
            if(path.equals("sync/extended-snapshot")&&method.equals("GET")) { send(e,200,store.syncExtendedSnapshot()); return; }
            if(Set.of("sync/review","sync/merge").contains(path)&&method.equals("POST")) {
                byte[] bytes=e.getRequestBody().readNBytes(SyncStore.MAX_BYTES*2+1);if(bytes.length>SyncStore.MAX_BYTES*2)throw new IllegalArgumentException("合并资料过大");
                var request=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8)));var local=Json.object(request.get("local"));var remote=Json.object(request.get("remote"));
                send(e,200,path.equals("sync/review")?SyncReview.differences(local,remote):SyncReview.merge(local,remote,Json.object(request.get("choices"))));return;
            }
            if(path.equals("sync/validate")&&method.equals("POST")||path.equals("sync/import")&&method.equals("POST")) {
                byte[] bytes=e.getRequestBody().readNBytes(SyncStore.MAX_BYTES+1);
                if(bytes.length>SyncStore.MAX_BYTES) { send(e,413,Map.of("error","同步资料超过 64 MiB")); return; }
                var request=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8)));
                var snapshot=Json.object(request.get("snapshot")); SyncStore.validate(snapshot);
                if(path.equals("sync/validate")) send(e,200,Map.of("valid",true));
                else { var result=store.syncImport(Json.str(request,"expectedHash"),snapshot); jobs=new CompanyJobs(store); send(e,200,result); } return;
            }
            if(Set.of("favorites/collect","favorites/collect-detail","applications/open","applications/find").contains(path)&&method.equals("POST")) {
                byte[] bytes=e.getRequestBody().readNBytes(2_000_001);
                if(bytes.length>2_000_000) { send(e,413,Map.of("error","Document exceeds 2 MB")); return; }
                var job=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8)));validate("favorites",job);
                send(e,200,switch(path) {case "favorites/collect"->store.collectFavorite(job);case "favorites/collect-detail"->store.collectFavoriteDetail(job);case "applications/find"->store.findApplication(job);default->store.openApplication(job);});return;
            }
            if(Set.of("resume-check","capture-preview").contains(path)&&method.equals("POST")) {
                byte[] bytes=e.getRequestBody().readNBytes(2_000_001);
                if(bytes.length>2_000_000) { send(e,413,Map.of("error","Document exceeds 2 MB")); return; }
                var doc=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8)));
                if(path.equals("resume-check")) { validate("resumes",doc); send(e,200,ResumeAdvisor.check(doc)); }
                else send(e,200,JobCapture.preview(doc)); return;
            }
            if(path.equals("jobs")&&method.equals("GET")) {
                var q=query(e.getRequestURI().getRawQuery()); int page=Integer.parseInt(q.getOrDefault("page","1"));
                if(page<1||page>100) throw new IllegalArgumentException("Invalid page");
                send(e,200,jobs.search(q.getOrDefault("provider","all"),q.getOrDefault("keyword",""),q.getOrDefault("city",""),page)); return;
            }
            String[] parts=path.split("/",-1); String kind=parts[0];
            if(kind.equals("resumes")&&parts.length>=3&&parts[2].equals("history")) {
                if(parts.length==3&&method.equals("GET")) { send(e,200,store.history(parts[1])); return; }
                if(parts.length==3&&method.equals("POST")) { store.checkpoint(parts[1],"手动保存"); send(e,200,Map.of("saved",true)); return; }
                if(parts.length==4&&method.equals("GET")) { send(e,200,store.revision(parts[1],parts[3])); return; }
            }
            if(Set.of("resumes","favorites","applications","sources","schedule","interviews").contains(kind)) {
                if(parts.length==1&&method.equals("GET")) { send(e,200,store.list(kind)); return; }
                if(parts.length==1&&method.equals("POST")) {
                    int limit=kind.equals("interviews")?4_000_000:2_000_000;
                    byte[] bytes=e.getRequestBody().readNBytes(limit+1);
                    if(bytes.length>limit) { send(e,413,Map.of("error","Document exceeds "+(limit/1_000_000)+" MB")); return; }
                    var doc=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8))); validate(kind,doc);
                    send(e,200,store.save(kind,doc)); return;
                }
                if(parts.length==2&&method.equals("DELETE")) { store.delete(kind,parts[1]); send(e,200,Map.of("deleted",true)); return; }
            }
            send(e,404,Map.of("error","Route not found"));
        } catch(ConcurrentModificationException ex) { try { send(e,409,Map.of("error",Objects.toString(ex.getMessage(),"同步冲突"))); } catch(Exception ignored) {} }
        catch(IllegalArgumentException ex) { try { send(e,400,Map.of("error",Objects.toString(ex.getMessage(),"Invalid input"))); } catch(Exception ignored) {} }
        catch(Exception ex) { ex.printStackTrace(System.err); try { send(e,500,Map.of("error","无法完成请求，请查看后端日志")); } catch(Exception ignored) {} }
        finally { e.close(); }
    }
    static void validate(String kind,Map<String,Object> doc) {
        if(doc.containsKey("tags")&&(!(doc.get("tags") instanceof String)||Json.str(doc,"tags").length()>700))throw new IllegalArgumentException("标签内容无效");
        for(String dateField:List.of("deadlineAt","lastFollowedAt"))if(doc.containsKey(dateField)&&!Json.str(doc,dateField).isEmpty())try{java.time.OffsetDateTime.parse(Json.str(doc,dateField));}catch(Exception ex){throw new IllegalArgumentException("日期字段无效："+dateField);}
        if(kind.equals("interviews")) { InterviewStudy.validate(doc); return; }
        if(kind.equals("sources")) { CompanySources.validate(doc); return; }
        if(kind.equals("schedule")) {
            for(String field:List.of("title","kind","startAt","notes","applicationId")) if(doc.containsKey(field)&&!(doc.get(field) instanceof String)) throw new IllegalArgumentException("日程文本字段无效");
            if(Json.str(doc,"title").isBlank()||Json.str(doc,"title").length()>200) throw new IllegalArgumentException("日程标题应为 1–200 个字符");
            if(!Set.of("笔试","面试","投递截止","跟进","自定义").contains(Json.str(doc,"kind"))) throw new IllegalArgumentException("日程类型无效");
            try { java.time.OffsetDateTime.parse(Json.str(doc,"startAt")); } catch(java.time.format.DateTimeParseException ex) { throw new IllegalArgumentException("日程日期和时间无效"); }
            if(Json.str(doc,"notes").length()>10000) throw new IllegalArgumentException("日程备注过长");
            for(String field:List.of("allDay","completed","reminderEnabled")) if(!(doc.get(field) instanceof Boolean)) throw new IllegalArgumentException("日程状态无效");
            if(!(doc.get("reminderMinutes") instanceof Number minutes)||!Set.of(0,15,60,1440).contains(minutes.intValue())||minutes.doubleValue()!=minutes.intValue()) throw new IllegalArgumentException("提醒时间无效");
            String linked=Json.str(doc,"applicationId"); if(!linked.isBlank()&&!linked.matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("关联投递记录无效");
            return;
        }
        if(kind.equals("resumes")) {
            ResumeLayout.validate(doc);
            if(doc.containsKey("fontFamily")&&!Set.of("MiSans","Microsoft YaHei","SimSun","Arial","Times New Roman").contains(Json.str(doc,"fontFamily"))) throw new IllegalArgumentException("简历字体无效");
            if(Json.str(doc,"title").isBlank()) throw new IllegalArgumentException("简历名称不能为空");
            if(Templates.all().stream().noneMatch(t->Json.str(t,"id").equals(Json.str(doc,"templateId")))) throw new IllegalArgumentException("Unknown template");
            var profile=Json.object(doc.get("profile"));
            for(String key:List.of("name","role","phone","email","city","link","summary")) {
                if(profile.containsKey(key)&&!(profile.get(key) instanceof String)) throw new IllegalArgumentException("Invalid profile field: "+key);
            }
            var sections=Json.array(doc.get("sections"));
            if(sections.size()>30) throw new IllegalArgumentException("Too many sections");
            sections.forEach(s->{ var section=Json.object(s);
                if(!(section.get("title") instanceof String)||!(section.get("content") instanceof String)||!(section.get("visible") instanceof Boolean)) throw new IllegalArgumentException("Invalid section");
                if(Json.str(section,"content").length()>30000) throw new IllegalArgumentException("Section too long");
            });
            String photo=Json.str(doc,"photo");
            if(!photo.isEmpty()&&!photo.matches("data:image/(png|jpeg);base64,[A-Za-z0-9+/=]+")) throw new IllegalArgumentException("Invalid photo");
        } else {
            URI uri=URI.create(Json.str(doc,"url"));
            if(!"https".equals(uri.getScheme())||uri.getHost()==null||uri.getUserInfo()!=null) throw new IllegalArgumentException("Job URL must be HTTPS");
            if(Json.str(doc,"title").isBlank()) throw new IllegalArgumentException("Job title is required");
            if(kind.equals("applications")) {
                if(!Set.of("已打开","已投递","笔试中","面试中","已录用","已结束").contains(Json.str(doc,"status"))) throw new IllegalArgumentException("Invalid application status");
                String due=Json.str(doc,"dueAt"); if(!due.isBlank()) try { java.time.OffsetDateTime.parse(due); } catch(java.time.format.DateTimeParseException ex) { throw new IllegalArgumentException("安排时间格式无效"); }
                if(Json.str(doc,"review").length()>10000) throw new IllegalArgumentException("复盘内容过长");
                if(doc.get("resumeSnapshot")!=null) validate("resumes",Json.object(doc.get("resumeSnapshot")));
            }
        }
    }
    private static Map<String,String> query(String raw) {
        Map<String,String> map=new HashMap<>(); if(raw!=null) for(String item:raw.split("&")) { String[] kv=item.split("=",2); map.put(URLDecoder.decode(kv[0],StandardCharsets.UTF_8),kv.length==2?URLDecoder.decode(kv[1],StandardCharsets.UTF_8):""); } return map;
    }
    private static void send(HttpExchange e,int status,Object body) throws Exception {
        byte[] bytes=Json.write(body).getBytes(StandardCharsets.UTF_8); e.getResponseHeaders().set("Content-Type","application/json; charset=utf-8"); e.getResponseHeaders().set("Cache-Control","no-store"); e.getResponseHeaders().set("X-Content-Type-Options","nosniff"); e.sendResponseHeaders(status,bytes.length); e.getResponseBody().write(bytes);
    }
    public static void main(String[] args) throws Exception {
        Map<String,String> options=new HashMap<>(); for(int i=0;i+1<args.length;i+=2) options.put(args[i],args[i+1]);
        var app=new Server(Integer.parseInt(options.getOrDefault("--port","19427")),Path.of(options.getOrDefault("--data","data")),options.getOrDefault("--token",""));
        app.start(); Runtime.getRuntime().addShutdownHook(new Thread(app::stop));
        System.out.println("ResumeStudio backend listening on 127.0.0.1:"+app.port());
    }
}
