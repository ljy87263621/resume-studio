package com.resumestudio;

import java.util.*;
import java.util.regex.*;

/** Optional layout fields retain compatibility with older resume documents. */
public final class ResumeLayout {
    private ResumeLayout(){}
    static final Pattern IMAGE=Pattern.compile("^!\\[图片\\]\\(resume-image:([A-Za-z0-9_-]{1,80})\\)$");
    static double number(Map<String,Object> doc,String field,double fallback,double min,double max){Object value=doc.get(field);return value instanceof Number n&&Double.isFinite(n.doubleValue())?Math.clamp(n.doubleValue(),min,max):fallback;}
    static boolean validImage(String data){
        if(data.length()>1_000_000||!data.matches("data:image/(png|jpeg|webp);base64,[A-Za-z0-9+/]+={0,2}"))return false;
        try{return Base64.getDecoder().decode(data.substring(data.indexOf(',')+1)).length>0;}catch(IllegalArgumentException invalid){return false;}
    }
    private static void text(Map<String,Object> doc,String field,int limit){if(doc.containsKey(field)&&(!(doc.get(field) instanceof String value)||value.length()>limit))throw new IllegalArgumentException("排版字段无效或过长："+field);}
    private static List<Object> array(Map<String,Object> doc,String field,int limit){if(!doc.containsKey(field))return List.of();if(!(doc.get(field) instanceof List<?>))throw new IllegalArgumentException("排版列表无效："+field);var items=Json.array(doc.get(field));if(items.size()>limit)throw new IllegalArgumentException("排版列表过长："+field);return items;}
    static void validate(Map<String,Object> resume){
        for(String field:List.of("pageMarginVertical","pageMarginHorizontal","sectionSpacing")) {
            if(!resume.containsKey(field))continue;double min=field.equals("sectionSpacing")?0:8,max=field.equals("sectionSpacing")?12:25;
            if(!(resume.get(field) instanceof Number n)||!Double.isFinite(n.doubleValue())||n.doubleValue()<min||n.doubleValue()>max)throw new IllegalArgumentException("页边距或模块间距无效："+field);
        }
        if(resume.containsKey("headerAlignment")&&!Set.of("template","left","center").contains(Json.str(resume,"headerAlignment")))throw new IllegalArgumentException("页眉对齐方式无效");
        for(Object value:array(Json.object(resume.get("profile")),"items",16)){var item=Json.object(value);text(item,"emoji",16);text(item,"label",60);text(item,"value",500);}
        for(Object value:Json.array(resume.get("sections"))){
            var section=Json.object(value);Set<String> ids=new HashSet<>();
            for(Object entry:array(section,"images",12)){
                var image=Json.object(entry);String id=Json.str(image,"id"),data=Json.str(image,"dataUrl");
                if(!id.matches("[A-Za-z0-9_-]{1,80}")||!ids.add(id))throw new IllegalArgumentException("图片标识无效或重复");
                text(image,"dataUrl",1_000_000);text(image,"alt",500);text(image,"caption",1000);
                for(String field:List.of("widthPercent","maxHeightMm"))if(image.containsKey(field)){
                    double min=field.equals("widthPercent")?10:20,max=field.equals("widthPercent")?100:170;
                    if(!(image.get(field) instanceof Number n)||!Double.isFinite(n.doubleValue())||n.doubleValue()<min||n.doubleValue()>max)throw new IllegalArgumentException("图片尺寸无效："+field);
                }
                if(!data.isEmpty()&&!validImage(data))throw new IllegalArgumentException("模块图片只支持本机 PNG、JPEG 或 WebP 数据");
            }
        }
    }
    static boolean tableLine(String line){String value=line.trim();return value.length()>=2&&value.startsWith("|")&&value.endsWith("|");}
    static List<String> cells(String line){
        String text=line.trim();text=text.substring(1,text.length()-1);List<String> cells=new ArrayList<>();StringBuilder cell=new StringBuilder();
        for(int i=0;i<text.length();i++){
            char c=text.charAt(i);if(c=='\\'&&i+1<text.length()&&(text.charAt(i+1)=='\\'||text.charAt(i+1)=='|')){cell.append(text.charAt(++i));continue;}
            if(c=='|'){cells.add(decodeCell(cell.toString().trim()));cell.setLength(0);}else cell.append(c);
        }cells.add(decodeCell(cell.toString().trim()));return cells;
    }
    private static String decodeCell(String value){return value.replace("&#10;","\n").replace("&amp;","&");}
}
