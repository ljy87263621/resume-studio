package com.resumestudio;
import java.nio.file.*;
import java.time.*;
import java.util.*;
public final class BackupCatalog {
    private BackupCatalog(){}
    public static void info(Path root,String name,String device,String source)throws java.io.IOException{
        Path file=root.resolve("sync-backups").resolve(name+".info");Files.writeString(file,Json.write(Map.of("device",device,"source",source,"at",Instant.now().toString())));
    }
    public static String device(Path root){try{return Files.readString(root.resolve("device-name.txt")).trim();}catch(Exception ex){return "本机设备";}}
    public static String importBytes(Path root,byte[] bytes)throws java.io.IOException{
        if(bytes.length>SyncStore.MAX_BYTES+8192)throw new java.io.IOException("备份超过 64 MiB");var document=Json.object(Json.parse(new String(bytes,java.nio.charset.StandardCharsets.UTF_8)));if(document.containsKey("backupVersion")&&!"1".equals(String.valueOf(document.get("backupVersion"))))throw new IllegalArgumentException("备份版本不兼容");var data=document.containsKey("backupVersion")?Json.object(document.get("snapshot")):document;SyncStore.validate(data);
        String name="import-"+UUID.randomUUID()+".json";Files.createDirectories(root.resolve("sync-backups"));Files.writeString(root.resolve("sync-backups").resolve(name),Json.write(data));info(root,name,document.containsKey("backupVersion")?Json.str(document,"device"):"未知设备（旧版备份）","手动导入");return name;
    }
    public static String display(Map<String,Object> document){
        String text=Json.write(document).replaceAll("data:image/[a-zA-Z0-9.+-]+;base64,[a-zA-Z0-9+/=]+","（内嵌照片，预览省略）");
        return text.length()>20000?text.substring(0,20000)+"\n（预览截断，JSON 导出保留完整内容）":text;
    }
    public static Map<String,Object> read(Path root,String name)throws java.io.IOException{
        if(!name.matches("[a-zA-Z0-9_.-]{1,200}\\.json"))throw new IllegalArgumentException("备份名称无效");Path file=root.resolve("sync-backups").resolve(name);
        if(Files.isSymbolicLink(file)||Files.size(file)>SyncStore.MAX_BYTES)throw new java.io.IOException("备份文件无效或过大");var data=Json.object(Json.parse(Files.readString(file)));SyncStore.validate(data);return data;
    }
    public static Object call(Path root,Store store,Map<String,Object> request)throws java.io.IOException{
        String operation=Json.str(request,"operation"),name=Json.str(request,"name");
        if(operation.equals("import"))return Map.of("name",importBytes(root,Base64.getDecoder().decode(Json.str(request,"base64"))));
        if(operation.equals("list")){
            Path directory=root.resolve("sync-backups");List<Object> result=new ArrayList<>();if(!Files.exists(directory))return result;
            try(var files=Files.list(directory)){for(Path file:files.filter(p->p.toString().endsWith(".json")).sorted(Comparator.reverseOrder()).toList()){
                try{var data=read(root,file.getFileName().toString());var row=new LinkedHashMap<String,Object>();row.put("name",file.getFileName().toString());row.put("at",Files.getLastModifiedTime(file).toInstant().toString());row.put("source",file.getFileName().toString().startsWith("merge-remote-")?"云端合并前":"本机替换前");row.put("device","未记录设备（历史备份）");Path metadata=file.resolveSibling(file.getFileName()+".info");if(!Files.isSymbolicLink(metadata)&&Files.isRegularFile(metadata)&&Files.size(metadata)<4096){var info=Json.object(Json.parse(Files.readString(metadata)));row.put("source",info.getOrDefault("source",row.get("source")));row.put("device",info.getOrDefault("device",row.get("device")));}var counts=new LinkedHashMap<String,Object>();for(String kind:List.of("resumes","favorites","applications","schedule","interviews"))counts.put(kind,Json.array(data.getOrDefault(kind,List.of())).size());row.put("counts",counts);result.add(row);}catch(Exception ignored){}
            }}result.sort(Comparator.comparing(row->Json.str(Json.object(row),"at")).reversed());return result;
        }
        var data=read(root,name);
        if(operation.equals("json"))return Map.of("json",Json.write(Map.of("backupVersion",1,"device",device(root),"exportedAt",Instant.now().toString(),"snapshot",data)));
        if(operation.equals("preview")){
            List<Object> rows=new ArrayList<>();for(String kind:List.of("resumes","favorites","applications","sources","schedule","interviews"))for(Object value:Json.array(data.getOrDefault(kind,List.of()))){var doc=Json.object(value);rows.add(Map.of("kind",kind,"id",Json.str(doc,"id"),"title",doc.getOrDefault("title",doc.getOrDefault("name","未命名")),"content",display(doc)));}
            return Map.of("records",rows,"expectedHash",Json.str(store.syncExtendedSnapshot(),"hash"));
        }
        if(operation.equals("restore"))return store.syncImport(Json.str(request,"expectedHash"),data);
        if(!operation.equals("restoreOne"))throw new IllegalArgumentException("备份操作无效");
        String kind=Json.str(request,"kind"),id=Json.str(request,"id");if(!List.of("resumes","favorites","applications","sources","schedule","interviews").contains(kind))throw new IllegalArgumentException("集合无效");
        Object selected=Json.array(data.getOrDefault(kind,List.of())).stream().filter(v->Json.str(Json.object(v),"id").equals(id)).findFirst().orElseThrow(()->new IllegalArgumentException("备份中没有这条记录"));
        var current=Json.object(store.syncExtendedSnapshot().get("snapshot"));var rows=Json.array(current.get(kind));rows.removeIf(v->Json.str(Json.object(v),"id").equalsIgnoreCase(id));rows.add(selected);
        if(kind.equals("resumes")){var history=Json.array(current.get("history"));history.removeIf(v->id.equalsIgnoreCase(Json.str(Json.object(v),"resumeId")));for(Object value:Json.array(data.get("history")))if(id.equalsIgnoreCase(Json.str(Json.object(value),"resumeId")))history.add(value);}
        return store.syncImport(Json.str(request,"expectedHash"),current);
    }
}
