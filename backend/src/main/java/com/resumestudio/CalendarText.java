package com.resumestudio;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
public final class CalendarText {
    private CalendarText(){}
    private static String escape(String value){return value.replace("\\","\\\\").replace("\r","").replace("\n","\\n").replace(";","\\;").replace(",","\\,");}
    public static String create(Store store)throws java.io.IOException{
        var rows=new ArrayList<Map<String,Object>>(store.list("schedule"));
        for(var app:store.list("applications"))if(!Json.str(app,"dueAt").isEmpty()&&!Boolean.TRUE.equals(app.get("reminderDone"))&&!Set.of("已录用","已结束").contains(Json.str(app,"status"))){var row=new LinkedHashMap<>(app);row.put("startAt",app.get("dueAt"));row.put("reminderEnabled",true);row.put("reminderMinutes",1440);rows.add(row);}
        List<String> lines=new ArrayList<>(List.of("BEGIN:VCALENDAR","VERSION:2.0","PRODID:-//ResumeStudio//Calendar//ZH","CALSCALE:GREGORIAN"));
        var format=DateTimeFormatter.ofPattern("yyyyMMdd'T'HHmmss'Z'").withZone(ZoneOffset.UTC);
        for(var row:rows){if(Boolean.TRUE.equals(row.get("completed")))continue;Instant at;try{at=OffsetDateTime.parse(Json.str(row,"startAt")).toInstant();}catch(Exception ex){continue;}
            lines.addAll(List.of("BEGIN:VEVENT","UID:"+escape(Json.str(row,"id"))+"@resumestudio.local","DTSTAMP:"+format.format(Instant.now()),Boolean.TRUE.equals(row.get("allDay"))?"DTSTART;VALUE=DATE:"+OffsetDateTime.parse(Json.str(row,"startAt")).toLocalDate().format(DateTimeFormatter.BASIC_ISO_DATE):"DTSTART:"+format.format(at),Boolean.TRUE.equals(row.get("allDay"))?"DTEND;VALUE=DATE:"+OffsetDateTime.parse(Json.str(row,"startAt")).toLocalDate().plusDays(1).format(DateTimeFormatter.BASIC_ISO_DATE):"DTEND:"+format.format(at.plusSeconds(3600)),"SUMMARY:"+escape(Json.str(row,"title")),"DESCRIPTION:"+escape(Json.str(row,"notes"))));
            if(Boolean.TRUE.equals(row.get("reminderEnabled")))lines.addAll(List.of("BEGIN:VALARM","ACTION:DISPLAY","DESCRIPTION:"+escape(Json.str(row,"title")),"TRIGGER:-PT"+row.getOrDefault("reminderMinutes",0)+"M","END:VALARM"));lines.add("END:VEVENT");
        }lines.add("END:VCALENDAR");StringBuilder result=new StringBuilder();for(String line:lines){int bytes=0;for(int offset=0;offset<line.length();){int cp=line.codePointAt(offset);String piece=new String(Character.toChars(cp));int length=piece.getBytes(java.nio.charset.StandardCharsets.UTF_8).length;if(bytes+length>73){result.append("\r\n ");bytes=1;}result.append(piece);bytes+=length;offset+=Character.charCount(cp);}result.append("\r\n");}return result.toString();
    }
}
