package com.resumestudio;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.util.*;
import java.util.zip.*;
import java.io.*;
public final class OptimizationTest {
    private OptimizationTest(){}
    private static void check(boolean value,String name){if(!value)throw new AssertionError(name);System.out.println("PASS optimization: "+name);}
    private static Map<String,Object> copy(Map<String,Object> value){return Json.object(Json.parse(Json.write(value)));}
    public static void main(String[] args)throws Exception{
        Path root=Files.createTempDirectory("optimization-");Store store=new Store(root);
        var resume=new LinkedHashMap<String,Object>();resume.put("id","resume");resume.put("title","测试简历");resume.put("templateId","campus");resume.put("profile",Map.of("name","测试 & 名称","role","嵌入式工程师","email","a@example.com"));resume.put("sections",List.of(Map.of("title","项目","visible",true,"content","第一行\n第二行"),Map.of("title","隐藏","visible",false,"content","不应导出")));store.save("resumes",resume);
        store.save("favorites",new LinkedHashMap<>(Map.of("id","job","title","嵌入式岗位","url","https://example.org/job/1","company","测试公司","notes","保留我的备注")));
        var initial=store.syncExtendedSnapshot();var local=Json.object(initial.get("snapshot"));var remote=copy(local);Json.object(Json.array(remote.get("favorites")).get(0)).put("notes","云端独立修改");
        var diff=SyncReview.differences(local,remote);check(diff.size()==1,"only changed record appears in sync comparison");
        var merged=SyncReview.merge(local,remote,Map.of("favorites:job","remote"));check(Json.str(Json.object(Json.array(merged.get("favorites")).get(0)),"notes").equals("云端独立修改"),"explicit remote record chosen without dropping local resume");
        boolean missing=false;try{SyncReview.merge(local,remote,Map.of());}catch(IllegalArgumentException expected){missing=true;}check(missing,"merge rejects missing choices before mutation");
        var removed=copy(remote);removed.put("favorites",new ArrayList<>());check(Json.array(SyncReview.merge(local,removed,Map.of("favorites:job","remote")).get("favorites")).isEmpty(),"explicit deletion choice is respected");
        var search=Json.object(WorkspaceTools.call(store,Map.of("query","嵌入式")));check(((Number)search.get("total")).intValue()>=2,"global search finds business content");
        WorkspaceTools.call(store,Map.of("operation","batch","kind","favorites","ids",List.of("job"),"expectedHash",initial.get("hash"),"tags","重点, 面试","deadlineAt",OffsetDateTime.now().plusDays(1).toString()));
        var tagged=store.list("favorites").get(0);check(Json.str(tagged,"tags").contains("重点")&&Json.str(tagged,"notes").equals("保留我的备注"),"atomic batch tags retain personal notes");
        boolean stale=false;try{WorkspaceTools.call(store,Map.of("operation","batch","kind","favorites","ids",List.of("job"),"expectedHash",initial.get("hash"),"tags","不能写入"));}catch(ConcurrentModificationException expected){stale=true;}check(stale&&!Json.str(store.list("favorites").get(0),"tags").contains("不能写入"),"batch stale version cannot overwrite later data");
        var backups=Json.array(BackupCatalog.call(root,store,Map.of("operation","list")));check(!backups.isEmpty(),"batch writes produce restorable backups");String name=Json.str(Json.object(backups.get(0)),"name");
        var preview=Json.object(BackupCatalog.call(root,store,Map.of("operation","preview","name",name)));check(!Json.array(preview.get("records")).isEmpty(),"backup preview provides records and comparison hash");
        BackupCatalog.call(root,store,Map.of("operation","restoreOne","name",name,"kind","favorites","id","job","expectedHash",preview.get("expectedHash")));check(store.list("resumes").size()==1&&Json.str(store.list("favorites").get(0),"notes").equals("保留我的备注"),"single-record restore leaves unrelated resume intact");
        String exported=Json.str(Json.object(BackupCatalog.call(root,store,Map.of("operation","json","name",name))),"json");String imported=BackupCatalog.importBytes(root,exported.getBytes(StandardCharsets.UTF_8));check(BackupCatalog.read(root,imported).containsKey("resumes"),"exported backup envelope imports with source metadata");
        boolean traversal=false;try{BackupCatalog.read(root,"../settings.json");}catch(IllegalArgumentException expected){traversal=true;}check(traversal,"backup traversal rejected");
        Path emptyRoot=Files.createTempDirectory("empty-backup-");Store emptyStore=new Store(emptyRoot);var empty=emptyStore.syncExtendedSnapshot();emptyStore.syncImport(Json.str(empty,"hash"),local);var emptyBackups=Json.array(BackupCatalog.call(emptyRoot,emptyStore,Map.of("operation","list")));String emptyName=Json.str(Json.object(emptyBackups.get(0)),"name");var emptyPreview=Json.object(BackupCatalog.call(emptyRoot,emptyStore,Map.of("operation","preview","name",emptyName)));check(Json.array(emptyPreview.get("records")).isEmpty(),"empty backup can be previewed without invented records");BackupCatalog.call(emptyRoot,emptyStore,Map.of("operation","restore","name",emptyName,"expectedHash",emptyPreview.get("expectedHash")));check(emptyStore.list("resumes").isEmpty()&&emptyStore.list("favorites").isEmpty(),"explicit full restore accepts an empty backup and preserves a recovery copy");
        byte[] docx=ResumeDocx.create(resume);String xml="";try(ZipInputStream zip=new ZipInputStream(new ByteArrayInputStream(docx))){for(ZipEntry entry;(entry=zip.getNextEntry())!=null;)if(entry.getName().equals("word/document.xml"))xml=new String(zip.readAllBytes(),StandardCharsets.UTF_8);}
        javax.xml.parsers.DocumentBuilderFactory factory=javax.xml.parsers.DocumentBuilderFactory.newInstance();factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl",true);factory.newDocumentBuilder().parse(new ByteArrayInputStream(xml.getBytes(StandardCharsets.UTF_8)));check(xml.contains("测试 &amp; 名称")&&xml.contains("第二行")&&!xml.contains("不应导出"),"editable Word OOXML escapes content and excludes hidden sections");
        store.save("schedule",new LinkedHashMap<>(Map.of("id","event","title","面试,会议;第二行\n"+"中文".repeat(80),"kind","面试","startAt",OffsetDateTime.now().plusDays(1).toString(),"notes","地点\\会议室","allDay",false,"completed",false,"reminderEnabled",true,"reminderMinutes",15)));
        String calendar=CalendarText.create(store);check(calendar.contains("BEGIN:VALARM")&&calendar.contains("\\,")&&calendar.contains("\\n"),"calendar preserves reminders and escapes text");for(String line:calendar.split("\r\n"))check(line.getBytes(StandardCharsets.UTF_8).length<=75,"calendar UTF-8 line folding within 75 octets");
        Files.write(Path.of("artifacts/optimization-editable.docx"),docx);
    }
}
