package com.resumestudio;

import java.nio.file.*;
import java.util.*;

/** Offline persistence and migration coverage for resume history and applications. */
public final class WorkflowTest {
    private static int checks;
    private static void require(boolean condition,String message) { if(!condition) throw new AssertionError(message); checks++; }
    public static void main(String[] args) throws Exception {
        Path root=Files.createTempDirectory("resume-workflow-"); var store=new Store(root);
        var resume=new LinkedHashMap<String,Object>(Map.of("id","resume-one","title","原稿","templateId","campus","profile",Map.of("name","测试"),"sections",List.of(Map.of("title","技能","content","- C / Linux","visible",true)),"photo",""));
        Server.validate("resumes",resume); store.save("resumes",resume); require(store.history("resume-one").isEmpty(),"new document starts without history");
        resume.put("title","新版"); store.save("resumes",resume); var revisions=store.history("resume-one"); require(revisions.size()==1,"automatic previous checkpoint");
        require(Json.str(Json.object(store.revision("resume-one",Json.str(revisions.get(0),"id")).get("resume")),"title").equals("原稿"),"checkpoint contains pre-edit content");
        for(int i=0;i<5;i++) { resume.put("title","编辑"+i); store.save("resumes",resume); }
        require(store.history("resume-one").size()==1,"typing does not create unbounded revisions");
        for(int i=0;i<25;i++) store.checkpoint("resume-one","版本"+i);
        require(store.history("resume-one").size()==20,"history retains at most 20 compressed revisions");
        var restarted=new Store(root); require(restarted.history("resume-one").size()==20,"history survives restart");
        try {store.history("../escape"); throw new AssertionError("path traversal accepted");} catch(IllegalArgumentException expected) {checks++;}
        try {store.revision("resume-one","../escape"); throw new AssertionError("revision traversal accepted");} catch(IllegalArgumentException expected) {checks++;}
        var record=new LinkedHashMap<String,Object>(Map.of("id","application-one","title","嵌入式实习","url","https://example.com/job","status","已打开","resumeSnapshot",resume));
        record.remove("resumeSnapshot"); Server.validate("applications",record); var opened=store.save("applications",record);
        require(Json.array(opened.get("timeline")).size()==1,"opened record has a status event");
        record.put("status","已投递"); record.put("resumeSnapshot",resume); record.put("resumeTitle",Json.str(resume,"title")); record.put("dueAt","2026-10-20T09:30:00+08:00"); record.put("eventKind","笔试"); record.put("review","补习指针题");
        Server.validate("applications",record); var submitted=store.save("applications",record);
        require(Json.array(submitted.get("timeline")).size()==3,"status and dated arrangement append to timeline");
        resume.put("title","投递之后编辑"); record.put("resumeSnapshot",resume); record.put("resumeTitle","改写标题"); record.put("status","笔试中"); record.put("timeline",List.of());
        var changed=store.save("applications",record); require(!Json.str(Json.object(changed.get("resumeSnapshot")),"title").equals("投递之后编辑"),"submission snapshot stays immutable");
        require(!Json.str(changed,"resumeTitle").equals("改写标题"),"snapshot title stays consistent");
        var legacy=new LinkedHashMap<String,Object>(Map.of("id","application-one","title","嵌入式实习","url","https://example.com/job","status","面试中"));
        var migrated=store.save("applications",legacy); require(migrated.get("resumeSnapshot")!=null&&Json.array(migrated.get("timeline")).size()==5,"old client cannot erase snapshot or timeline");
        var invalid=new LinkedHashMap<>(record); invalid.put("dueAt","tomorrow"); try {Server.validate("applications",invalid); throw new AssertionError("invalid date accepted");} catch(IllegalArgumentException expected) {checks++;}
        store.delete("resumes","resume-one"); require(store.history("resume-one").isEmpty(),"deleting document removes its history");
        require(new Store(root).list("applications").get(0).get("resumeSnapshot")!=null,"deleted source resume leaves submitted snapshot intact");
        var job=new LinkedHashMap<String,Object>(Map.of("id","provider-position","title","Embedded","url","https://example.com/jobs/embedded?utm_source=search","notes","我的面试准备","source","官网"));
        require((Boolean)store.collectFavorite(job).get("created"),"first collection creates a favorite");
        var pasted=new LinkedHashMap<>(job);pasted.put("id","external-alias");pasted.put("url","https://example.com/jobs/embedded?utm_source=share");pasted.put("notes","");pasted.put("title","different title");
        var duplicate=store.collectFavorite(pasted);
        require(!(Boolean)duplicate.get("created")&&Json.str(Json.object(duplicate.get("record")),"notes").equals("我的面试准备"),"cross-platform repeated collect preserves handwritten notes and original title");
        require(store.list("favorites").size()==1,"canonical links do not create duplicate favorites");
        try {store.save("favorites",pasted);throw new AssertionError("conflicting favorite overwrite accepted");}catch(IllegalArgumentException expected){checks++;}
        require(store.findApplication(job).isEmpty(),"looking up an untracked job does not create an opened record");
        var tracked=Json.object(store.openApplication(job).get("record"));
        require(Json.str(tracked,"status").equals("已打开")&&!tracked.containsKey("resumeSnapshot")&&!tracked.containsKey("resumeTitle"),"opening a job never assumes the currently edited resume was submitted");
        tracked.put("status","面试中");tracked.put("notes","保留投递备注");store.save("applications",tracked);
        var reopened=store.openApplication(pasted);
        require(!(Boolean)reopened.get("created")&&Json.str(Json.object(reopened.get("record")),"status").equals("面试中"),"opening an alias does not duplicate or downgrade a submitted application");
        var tasks=new ArrayList<java.util.concurrent.Future<Map<String,Object>>>();var pool=java.util.concurrent.Executors.newFixedThreadPool(6);
        try {for(int i=0;i<20;i++)tasks.add(pool.submit(()->store.collectFavorite(pasted)));for(var task:tasks)require(!(Boolean)task.get().get("created"),"concurrent repeated favorite is idempotent");}
        finally {pool.shutdownNow();}
        require(store.list("applications").size()==2&&store.list("favorites").size()==1,"atomic collection and tracking keep one record per matched job");
        System.out.println("PASS: "+checks+" offline workflow checks; fixtures: "+root);
    }
}
