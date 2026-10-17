package com.resumestudio;

import java.util.*;

/** Conservative record-level merge: every difference requires an explicit choice, including deletions. */
public final class SyncReview {
    private SyncReview() {}
    private static Map<String,Map<String,Object>> records(Map<String,Object> snapshot) {
        Map<String,Map<String,Object>> rows=new LinkedHashMap<>();
        for(String kind:List.of("resumes","favorites","applications","sources","schedule","interviews","history")) {
            if(!snapshot.containsKey(kind))continue;
            for(Object value:Json.array(snapshot.get(kind))) {
                var doc=Json.object(value);String id=Json.str(doc,"id");
                if(kind.equals("history"))id=Json.str(doc,"resumeId")+"/"+id;
                rows.put(kind+":"+id,doc);
            }
        }
        rows.put("settings:preferences",Json.object(snapshot.get("settings")));return rows;
    }
    public static List<Object> differences(Map<String,Object> local,Map<String,Object> remote) {
        SyncStore.validate(local);SyncStore.validate(remote);
        var a=records(local);var b=records(remote);var keys=new LinkedHashSet<>(a.keySet());keys.addAll(b.keySet());List<Object> result=new ArrayList<>();
        for(String key:keys)if(!Objects.equals(a.get(key),b.get(key))) {
            var row=new LinkedHashMap<String,Object>();row.put("key",key);row.put("kind",key.substring(0,key.indexOf(':')));
            var doc=a.containsKey(key)?a.get(key):b.get(key);row.put("title",doc.getOrDefault("title",doc.getOrDefault("name",key)));
            row.put("local",a.containsKey(key)?BackupCatalog.display(a.get(key)):"（本机已删除或不存在）");row.put("remote",b.containsKey(key)?BackupCatalog.display(b.get(key)):"（云端已删除或不存在）");result.add(row);
        }
        return result;
    }
    public static Map<String,Object> merge(Map<String,Object> local,Map<String,Object> remote,Map<String,Object> choices) {
        var differences=differences(local,remote);var a=records(local);var b=records(remote);var keys=new LinkedHashSet<>(a.keySet());keys.addAll(b.keySet());
        Set<String> required=new HashSet<>();for(Object value:differences)required.add(Json.str(Json.object(value),"key"));
        if(!choices.keySet().equals(required))throw new IllegalArgumentException("请为每项差异明确选择本机或云端");
        var result=Json.object(Json.parse(Json.write(local)));
        for(String kind:List.of("resumes","favorites","applications","sources","schedule","interviews","history"))if(local.containsKey(kind)||remote.containsKey(kind))result.put(kind,new ArrayList<>());
        for(String key:keys) {
            String choice=required.contains(key)?Json.str(choices,key):"local";
            if(!choice.equals("local")&&!choice.equals("remote"))throw new IllegalArgumentException("合并方向无效");
            var value=(choice.equals("local")?a:b).get(key);if(value==null)continue;
            String kind=key.substring(0,key.indexOf(':'));
            if(kind.equals("settings"))result.put("settings",value);else Json.array(result.get(kind)).add(value);
        }
        // Histories belonging to a deliberately deleted resume cannot be imported.
        Set<String> resumes=new HashSet<>();for(Object value:Json.array(result.get("resumes")))resumes.add(Json.str(Json.object(value),"id"));
        Json.array(result.get("history")).removeIf(value->!resumes.contains(Json.str(Json.object(value),"resumeId")));
        SyncStore.validate(result);return result;
    }
}
