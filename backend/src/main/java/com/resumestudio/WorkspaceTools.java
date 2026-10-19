package com.resumestudio;
import java.time.*;
import java.util.*;
public final class WorkspaceTools {
    private WorkspaceTools(){}
    private static String tags(String text){Set<String> tags=new LinkedHashSet<>();for(String value:text.split("[,，;；\\n]+")){String tag=value.trim();if(tag.length()>30)throw new IllegalArgumentException("每个标签最多 30 字");if(!tag.isEmpty())tags.add(tag);}if(tags.size()>20)throw new IllegalArgumentException("最多 20 个标签");return String.join(", ",tags);}
    public static Object call(Store store,Map<String,Object> request)throws java.io.IOException{
        synchronized(store){
            var state=store.syncExtendedSnapshot();var data=Json.object(state.get("snapshot"));String operation=Json.str(request,"operation");
            if(operation.equals("batch")){
                String kind=Json.str(request,"kind");if(!List.of("favorites","applications").contains(kind))throw new IllegalArgumentException("批量操作仅用于收藏和投递");
                Set<String> ids=new HashSet<>();for(Object value:Json.array(request.get("ids")))ids.add(String.valueOf(value));if(ids.isEmpty()||ids.size()>100)throw new IllegalArgumentException("请选择 1–100 条记录");
                String status=Json.str(request,"status"),labels=Json.str(request,"tags"),deadline=Json.str(request,"deadlineAt"),now=Instant.now().toString();
                int changed=0;for(Object value:Json.array(data.get(kind))){var row=Json.object(value);if(!ids.contains(Json.str(row,"id")))continue;changed++;
                    if(!labels.isBlank())row.put("tags",tags(labels));
                    if(Boolean.TRUE.equals(request.get("clearTags")))row.put("tags","");
                    if(!deadline.isEmpty()){OffsetDateTime.parse(deadline);row.put("deadlineAt",deadline);}else if(Boolean.TRUE.equals(request.get("clearDeadline")))row.put("deadlineAt","");
                    if(Boolean.TRUE.equals(request.get("followUp")))row.put("lastFollowedAt",now);
                    if(!status.isEmpty()&&kind.equals("applications")&&!status.equals(Json.str(row,"status"))){var events=Json.array(row.get("timeline"));events.add(Map.of("status",status,"at",now,"note","批量更新进度"));row.put("timeline",new ArrayList<>(events.subList(Math.max(0,events.size()-100),events.size())));row.put("status",status);}
                    row.put("updatedAt",now);Server.validate(kind,row);
                }
                if(changed!=ids.size())throw new IllegalArgumentException("选中的记录已变化，请刷新");store.syncImport(Json.str(request,"expectedHash"),data);return Map.of("changed",changed);
            }
            String query=Json.str(request,"query").trim().toLowerCase(Locale.ROOT);if(query.length()>200)throw new IllegalArgumentException("搜索词过长");String kindFilter=Json.str(request,"kind");List<Object> rows=new ArrayList<>();Instant now=Instant.now();
            for(String kind:List.of("resumes","favorites","applications","sources","schedule","interviews")){
                if(!kindFilter.isEmpty()&&!kindFilter.equals(kind))continue;
                for(Object value:Json.array(data.get(kind))){var row=Json.object(value);String text=Json.str(row,"title")+" "+Json.str(row,"company")+" "+Json.str(row,"name")+" "+Json.str(row,"notes")+" "+Json.str(row,"description")+" "+Json.str(row,"tags");if(kind.equals("resumes"))text+=" "+Json.write(row.get("profile"))+" "+Json.write(row.get("sections"));if(kind.equals("interviews"))text+=" "+Json.write(row.get("questions"));
                    if(!query.isEmpty()&&!text.toLowerCase(Locale.ROOT).contains(query))continue;
                    var item=new LinkedHashMap<String,Object>();item.put("kind",kind);item.put("id",row.get("id"));item.put("title",row.getOrDefault("title",row.getOrDefault("name","未命名")));item.put("tags",Json.str(row,"tags"));item.put("summary",text.substring(0,Math.min(250,text.length())));item.put("updatedAt",Json.str(row,"updatedAt"));String alert="";
                    String deadline=Json.str(row,"deadlineAt");if(!deadline.isEmpty())try{Instant due=OffsetDateTime.parse(deadline).toInstant();if(due.isBefore(now))alert="已过截止时间";else if(due.isBefore(now.plusSeconds(3*86400)))alert="三天内截止";}catch(Exception ignored){}
                    if(kind.equals("applications")&&!List.of("已录用","已结束").contains(Json.str(row,"status")))try{String last=Json.str(row,"lastFollowedAt");if(last.isEmpty())last=Json.str(row,"createdAt");if(!last.isEmpty()&&OffsetDateTime.parse(last).toInstant().isBefore(now.minusSeconds(7*86400)))alert+=(alert.isEmpty()?"":" · ")+"超过七天未跟进";}catch(Exception ignored){}
                    item.put("alert",alert);rows.add(item);
                }
            }
            int total=rows.size();return Map.of("hash",state.get("hash"),"total",total,"rows",rows.subList(0,Math.min(200,total)));
        }
    }
}
