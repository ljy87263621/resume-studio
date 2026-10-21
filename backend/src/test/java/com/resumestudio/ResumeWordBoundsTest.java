package com.resumestudio;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;

/** Run with a small heap to catch media and sparse-table memory amplification. */
public final class ResumeWordBoundsTest {
    private ResumeWordBoundsTest(){}
    private static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);System.out.println("PASS Word bounds: "+label);}
    private static Map<String,Object> document(List<Object> sections){return Map.of("id","bounds","title","内存边界","templateId","campus","profile",Map.of("name","测试"),"sections",sections);}
    private static Map<String,Object> section(String content,List<Object> images){return Map.of("title","作品","content",content,"visible",true,"images",images);}
    private static Map<String,byte[]> entries(byte[] bytes)throws IOException {
        Map<String,byte[]> result=new HashMap<>();
        try(var zip=new ZipInputStream(new ByteArrayInputStream(bytes))){for(ZipEntry entry;(entry=zip.getNextEntry())!=null;)result.put(entry.getName(),zip.readAllBytes());}return result;
    }
    public static void main(String[] args)throws Exception {
        byte[] png=Arrays.copyOf(Base64.getDecoder().decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGNkSLnTwMDAwMAEIkAYABpaAcSeBhM1AAAAAElFTkSuQmCC"),600_000);
        String data="data:image/png;base64,"+Base64.getEncoder().encodeToString(png);
        var first=Map.<String,Object>of("id","one","dataUrl",data,"caption","第一张");
        var second=Map.<String,Object>of("id","two","dataUrl",data,"widthPercent",20,"caption","另一种尺寸");
        var resume=document(List.of(section("![图片](resume-image:one)\n".repeat(160),List.of(first)),section("![图片](resume-image:two)",List.of(second))));
        Server.validate("resumes",resume);
        check(Json.write(resume).getBytes(StandardCharsets.UTF_8).length<2_000_000,"repeated-reference fixture fits the stored document limit");
        var exported=entries(ResumeDocx.create(resume));
        check(exported.keySet().stream().filter(path->path.startsWith("word/media/")).count()==1&&Arrays.equals(exported.get("word/media/image1.png"),png),"161 placements across sections reuse one exact image payload under a 64 MiB heap");
        String xml=new String(exported.get("word/document.xml"),StandardCharsets.UTF_8);
        var matcher=Pattern.compile("<wp:docPr id=\"(\\d+)\"").matcher(xml);Set<String> ids=new HashSet<>();int drawings=0;
        boolean unique=true;while(matcher.find()){unique&=ids.add(matcher.group(1));drawings++;}check(unique&&drawings==161,"reused media still have unique drawing identifiers");
        check(drawings==161&&xml.contains("另一种尺寸")&&!xml.contains("r:embed=\"rId2\""),"all placements and independent captions retain the shared media relationship");
        png[png.length-1]=1;
        var different=Map.<String,Object>of("id","different","dataUrl","data:image/png;base64,"+Base64.getEncoder().encodeToString(png));
        var distinct=entries(ResumeDocx.create(document(List.of(section("![图片](resume-image:one)\n![图片](resume-image:different)",List.of(first,different))))));
        check(distinct.containsKey("word/media/image2.png"),"different image bytes keep separate media relationships");
        String sparse="| x "+"| x ".repeat(100)+"|\n"+"| one |\n".repeat(200);
        var oversized=document(List.of(section(sparse,List.of())));Server.validate("resumes",oversized);boolean rejected=false;
        try{ResumeDocx.create(oversized);}catch(IOException expected){rejected=expected.getMessage().contains("表格过大");}
        check(rejected,"small sparse input stops before allocating a huge rectangular table");
        check(ResumeDocx.create(document(List.of(section("| A | B |\n| --- | --- |\n| one |",List.of())))).length>0,"ordinary ragged tables still export");
    }
}
