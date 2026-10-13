package com.resumestudio;

import com.sun.net.httpserver.*;
import java.net.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

public final class Server {
    private final Store store; private final CompanyJobs jobs; private final String token;
    private final HttpServer server;
    public Server(int port,Path data,String token) throws Exception {
        if(token.length()<16) throw new IllegalArgumentException("Provide a session token of at least 16 characters");
        this.token=token; store=new Store(data); jobs=new CompanyJobs(store);
        server=HttpServer.create(new InetSocketAddress(InetAddress.getByName("127.0.0.1"),port),16);
        server.setExecutor(Executors.newFixedThreadPool(6)); server.createContext("/api/",this::handle);
    }
    public void start() { server.start(); }
    public int port() { return server.getAddress().getPort(); }
    public void stop() { server.stop(0); }
    private void handle(HttpExchange e) {
        try {
            if(!token.equals(e.getRequestHeaders().getFirst("X-Resume-Token"))) { send(e,401,Map.of("error","Unauthorized")); return; }
            String path=e.getRequestURI().getPath().substring(5), method=e.getRequestMethod();
            if(path.equals("health")&&method.equals("GET")) { send(e,200,Map.of("status","ok","version","0.6.3","java",System.getProperty("java.version"))); return; }
            if(path.equals("templates")&&method.equals("GET")) { send(e,200,Templates.all()); return; }
            if(path.equals("job-sources")&&method.equals("GET")) { send(e,200,jobs.sources()); return; }
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
            if(Set.of("resumes","favorites","applications","sources").contains(kind)) {
                if(parts.length==1&&method.equals("GET")) { send(e,200,store.list(kind)); return; }
                if(parts.length==1&&method.equals("POST")) {
                    byte[] bytes=e.getRequestBody().readNBytes(2_000_001);
                    if(bytes.length>2_000_000) { send(e,413,Map.of("error","Document exceeds 2 MB")); return; }
                    var doc=Json.object(Json.parse(new String(bytes,StandardCharsets.UTF_8))); validate(kind,doc);
                    send(e,200,store.save(kind,doc)); return;
                }
                if(parts.length==2&&method.equals("DELETE")) { store.delete(kind,parts[1]); send(e,200,Map.of("deleted",true)); return; }
            }
            send(e,404,Map.of("error","Route not found"));
        } catch(IllegalArgumentException ex) { try { send(e,400,Map.of("error",Objects.toString(ex.getMessage(),"Invalid input"))); } catch(Exception ignored) {} }
        catch(Exception ex) { ex.printStackTrace(System.err); try { send(e,500,Map.of("error","无法完成请求，请查看后端日志")); } catch(Exception ignored) {} }
        finally { e.close(); }
    }
    static void validate(String kind,Map<String,Object> doc) {
        if(kind.equals("sources")) { CompanySources.validate(doc); return; }
        if(kind.equals("resumes")) {
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
