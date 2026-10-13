package com.resumestudio;
import java.net.URI;
import java.net.http.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.Duration;
import java.util.*;

/** Exercise local HTTP CRUD, validation and persisted calendar state without external services. */
public final class ScheduleTest {
    private static int checks;
    private static void require(boolean ok,String label){if(!ok)throw new AssertionError(label);checks++;}
    public static void main(String[] args) throws Exception {
        Path root=Files.createTempDirectory("resume-schedule-");String token="schedule-local-test-token";
        var app=new Server(0,root,token);app.start();
        try(var client=HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build()) {
            String base="http://127.0.0.1:"+app.port()+"/api/schedule";
            var item=new LinkedHashMap<String,Object>(Map.of("id","schedule-one","title","嵌入式面试","kind","面试","startAt","2026-11-01T09:30:00+08:00","allDay",false,"completed",false,"reminderEnabled",true,"reminderMinutes",60,"notes","携带开发板","applicationId",""));
            var post=client.send(HttpRequest.newBuilder(URI.create(base)).header("X-Resume-Token",token).POST(HttpRequest.BodyPublishers.ofString(Json.write(item),StandardCharsets.UTF_8)).build(),HttpResponse.BodyHandlers.ofString());
            require(post.statusCode()==200,"HTTP save schedule");require(Json.str(Json.object(Json.parse(post.body())),"title").equals("嵌入式面试"),"UTF-8 HTTP response");
            require(new Store(root).list("schedule").size()==1,"calendar state survives Store restart");
            item.put("completed",true);Server.validate("schedule",item);new Store(root).save("schedule",item);require(Boolean.TRUE.equals(new Store(root).list("schedule").get(0).get("completed")),"completion survives restart");
            item.put("allDay",true);item.put("kind","投递截止");Server.validate("schedule",item);checks++;
            for(String field:List.of("title","kind","startAt","reminderMinutes","completed","applicationId")) {
                var bad=new LinkedHashMap<>(item);bad.put(field,switch(field){case "title"->" ";case "kind"->"unknown";case "startAt"->"2026-13-01";case "reminderMinutes"->1.5;case "completed"->"true";default->"../outside";});
                var response=client.send(HttpRequest.newBuilder(URI.create(base)).header("X-Resume-Token",token).POST(HttpRequest.BodyPublishers.ofString(Json.write(bad))).build(),HttpResponse.BodyHandlers.ofString());
                require(response.statusCode()==400,"reject invalid "+field);
            }
            var list=client.send(HttpRequest.newBuilder(URI.create(base)).header("X-Resume-Token",token).GET().build(),HttpResponse.BodyHandlers.ofString());require(list.statusCode()==200&&Json.array(Json.parse(list.body())).size()==1,"HTTP list existing event");
            var unauthorized=client.send(HttpRequest.newBuilder(URI.create(base)).GET().build(),HttpResponse.BodyHandlers.ofString());require(unauthorized.statusCode()==401,"calendar API remains authenticated");
            var deleted=client.send(HttpRequest.newBuilder(URI.create(base+"/schedule-one")).header("X-Resume-Token",token).DELETE().build(),HttpResponse.BodyHandlers.ofString());require(deleted.statusCode()==200&&new Store(root).list("schedule").isEmpty(),"HTTP delete event");
            require(new Store(root).list("applications").isEmpty(),"calendar CRUD does not create application records");
        } finally {app.stop();}
        System.out.println("PASS: "+checks+" schedule HTTP, validation and persistence checks");
    }
}
