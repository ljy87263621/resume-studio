package com.resumestudio;

import java.nio.file.*;
import java.util.*;

/** Offline tests: import validates before mutation and preserves raw user data. */
public final class SyncStoreTest {
    static void check(boolean condition,String label) { if(!condition) throw new AssertionError(label); }
    static Map<String,Object> resume(String id) {
        var doc=new LinkedHashMap<String,Object>(); doc.put("id",id); doc.put("title","同步测试"); doc.put("templateId","campus");
        doc.put("profile",Map.of("name","张三")); doc.put("sections",List.of()); doc.put("futureField",Map.of("retained",true)); return doc;
    }
    public static void main(String[] args) throws Exception {
        Path root=Files.createTempDirectory("resume-sync-test-"); Store store=new Store(root);
        Files.writeString(root.resolve("settings.json"),"{\"theme\":\"dark\",\"apiKey\":\"LOCAL-ONLY\"}");
        store.save("resumes",resume("one")); store.checkpoint("one","第一版");
        var before=store.syncSnapshot(); var data=Json.object(before.get("snapshot"));
        check(!Json.object(data.get("settings")).containsKey("apiKey"),"secret excluded");
        check(Json.array(data.get("history")).size()==1,"history included");
        var badTitle=resume("one"); badTitle.put("title",123); rejects(store,before,"resumes",badTitle,"numeric resume title");
        var badFont=resume("one"); badFont.put("fontSize","x"); rejects(store,before,"resumes",badFont,"text font size");
        var badTimeline=new LinkedHashMap<String,Object>(Map.of("id","app","title","职位","url","https://example.org","status","已投递","timeline","bad"));
        rejects(store,before,"applications",badTimeline,"text application timeline");
        var badNested=resume("snapshot"); badNested.put("lineHeight",true); badTimeline.put("timeline",List.of()); badTimeline.put("resumeSnapshot",badNested);
        rejects(store,before,"applications",badTimeline,"nested snapshot types");
        var badFavorite=new LinkedHashMap<String,Object>(Map.of("id","favorite","title","职位","url","https://example.org","company",false));
        rejects(store,before,"favorites",badFavorite,"favorite company type");
        var badSource=new LinkedHashMap<String,Object>(Map.of("id","source","name","公司","adapter","jsonld","url","https://example.org","enabled","true"));
        rejects(store,before,"sources",badSource,"source enabled type");
        var badSchedule=new LinkedHashMap<String,Object>(Map.of("id","schedule","title","面试","kind","面试","startAt","2026-10-14T09:00:00+08:00","allDay",false,"completed",false,"reminderEnabled",true,"reminderMinutes",new java.math.BigDecimal("15.0")));
        rejects(store,before,"schedule",badSchedule,"fractional token for C# integer");
        badSchedule.put("reminderMinutes",new java.math.BigDecimal("2147483648")); rejects(store,before,"schedule",badSchedule,"C# integer overflow");
        var badSettings=new LinkedHashMap<>(data); badSettings.put("settings",Map.of("previewDelayMs",new java.math.BigDecimal("900.0")));
        try { store.syncImport(Json.str(before,"hash"),badSettings); throw new AssertionError("fractional settings integer accepted"); } catch(IllegalArgumentException expected) {}
        var badHistory=new LinkedHashMap<>(data); var badRevision=new LinkedHashMap<>(Json.object(Json.array(data.get("history")).getFirst())); var badRevisionResume=resume("one"); badRevisionResume.put("photo",false); badRevision.put("resume",badRevisionResume); badHistory.put("history",List.of(badRevision));
        try { store.syncImport(Json.str(before,"hash"),badHistory); throw new AssertionError("bad history resume types accepted"); } catch(IllegalArgumentException expected) {}
        var caseCollision=new LinkedHashMap<>(data); caseCollision.put("resumes",List.of(resume("one"),resume("ONE")));
        try { store.syncImport(Json.str(before,"hash"),caseCollision); throw new AssertionError("case-colliding IDs accepted"); } catch(IllegalArgumentException expected) {}
        check(Json.str(store.syncSnapshot(),"hash").equals(Json.str(before,"hash")),"case collision rejected before writes");
        var historyCollision=new LinkedHashMap<>(data); var revision=new LinkedHashMap<>(Json.object(Json.array(data.get("history")).getFirst())); revision.put("id","version");
        var otherRevision=new LinkedHashMap<>(revision); otherRevision.put("id","VERSION"); historyCollision.put("history",List.of(revision,otherRevision));
        try { store.syncImport(Json.str(before,"hash"),historyCollision); throw new AssertionError("case-colliding history versions accepted"); } catch(IllegalArgumentException expected) {}
        var invalid=new LinkedHashMap<>(data); invalid.put("favorites",List.of(Map.of("id","../escape","title","x","url","https://example.org")));
        try { store.syncImport(Json.str(before,"hash"),invalid); throw new AssertionError("invalid import accepted"); } catch(IllegalArgumentException expected) {}
        check(store.list("resumes").size()==1,"validation before writes");
        var replacement=new LinkedHashMap<>(data); replacement.put("resumes",List.of(resume("two"))); replacement.put("history",List.of());
        store.syncImport(Json.str(before,"hash"),replacement);
        check(Json.str(store.list("resumes").getFirst(),"id").equals("two"),"deletion propagated");
        check(store.list("resumes").getFirst().containsKey("futureField"),"unknown record fields retained");
        check(Files.readString(root.resolve("settings.json")).contains("LOCAL-ONLY"),"local secret retained");
        try { store.syncImport(Json.str(before,"hash"),data); throw new AssertionError("stale import accepted"); } catch(ConcurrentModificationException expected) {}
        // An interrupted multi-file import restores the durable pre-import snapshot on next startup.
        Files.writeString(root.resolve("sync-recovery.json"),Json.write(data));
        Store recovered=new Store(root); check(Json.str(recovered.list("resumes").getFirst(),"id").equals("one"),"journal recovery");
        check(recovered.history("one").size()==1,"history restored");
        check(!Files.exists(root.resolve("sync-recovery.json")),"journal removed after recovery");
        try(var backups=Files.list(root.resolve("sync-backups"))) { check(backups.findAny().isPresent(),"durable backup"); }
        System.out.println("PASS SyncStore: validation, CAS, deletion, extension fields, local secrets, history, recovery, backup");
    }
    static void rejects(Store store,Map<String,Object> before,String kind,Map<String,Object> document,String label) throws Exception {
        var bad=new LinkedHashMap<>(Json.object(before.get("snapshot"))); bad.put(kind,List.of(document));
        try { store.syncImport(Json.str(before,"hash"),bad); throw new AssertionError(label+" accepted"); } catch(IllegalArgumentException expected) {}
        check(Json.str(store.syncSnapshot(),"hash").equals(Json.str(before,"hash")),label+" rejected before writes");
    }
}
