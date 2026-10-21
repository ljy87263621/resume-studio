package com.resumestudio;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.zip.*;

public final class ResumeLayoutTest {
    private ResumeLayoutTest(){}
    private static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);System.out.println("PASS layout: "+label);}
    private static void rejects(Map<String,Object> resume,String label){boolean rejected=false;try{Server.validate("resumes",resume);}catch(IllegalArgumentException expected){rejected=true;}check(rejected,label);}
    public static void main(String[] args)throws Exception {
        var image=new LinkedHashMap<String,Object>(Map.of("id","diagram","dataUrl","data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGNkSLnTwMDAwMAEIkAYABpaAcSeBhM1AAAAAElFTkSuQmCC","alt","图 <安全>","caption","作品说明"));
        var section=new LinkedHashMap<String,Object>(Map.of("title","作品","visible",true,"content","### 子板块\n![图片](resume-image:diagram)\n| 类别 | 内容 |\n| --- | --- |\n| C\\|C++ | 多行&#10;文本 |","images",List.of(image)));
        var profile=new LinkedHashMap<String,Object>(Map.of("name","张三","items",List.of(Map.of("emoji","🎓","label","学历","value","本科"))));
        var resume=new LinkedHashMap<String,Object>(Map.of("id","rich","title","排版验证","templateId","campus","profile",profile,"sections",List.of(section),"pageMarginVertical",12,"pageMarginHorizontal",18,"sectionSpacing",5,"headerAlignment","center"));
        Server.validate("resumes",resume);
        byte[] bytes=ResumeDocx.create(resume);var entries=new HashMap<String,byte[]>();
        try(var zip=new ZipInputStream(new ByteArrayInputStream(bytes))){for(ZipEntry e;(e=zip.getNextEntry())!=null;)entries.put(e.getName(),zip.readAllBytes());}
        String xml=new String(entries.get("word/document.xml"),StandardCharsets.UTF_8);
        check(xml.contains("学历：本科")&&xml.contains("<w:tbl>")&&xml.contains("C|C++")&&xml.contains("<w:br/>")&&xml.contains("子板块"),"Word preserves contacts, directory, editable cells and multiline text");
        check(xml.contains("<w:drawing>")&&entries.containsKey("word/media/image1.png")&&entries.containsKey("word/_rels/document.xml.rels")&&!xml.contains("resume-image:"),"Word embeds local image bytes with relationships");
        check(xml.contains("w:top=\"680\"")&&xml.contains("w:right=\"1020\""),"Word respects custom A4 margins");
        var factory=javax.xml.parsers.DocumentBuilderFactory.newInstance();factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl",true);for(var entry:entries.entrySet())if(entry.getKey().endsWith(".xml")||entry.getKey().endsWith(".rels"))factory.newDocumentBuilder().parse(new ByteArrayInputStream(entry.getValue()));
        image.put("dataUrl","https://example.com/external.png");rejects(resume,"external section images rejected before storage");
        image.put("dataUrl","data:image/png;base64,notbase64");rejects(resume,"malformed image data rejected");image.put("dataUrl","");
        profile.put("items",List.of(Map.of("label","学历","value",123)));rejects(resume,"malformed contact values rejected");profile.put("items",List.of());
        resume.put("pageMarginVertical",100);rejects(resume,"out-of-range page margin rejected");resume.remove("pageMarginVertical");
        resume.put("sections",List.of(Map.of("title","旧模块","content","正文","visible",true)));Server.validate("resumes",resume);check(ResumeDocx.create(resume).length>0,"old schema remains accepted");
        Path output=Path.of("artifacts/layout-editable.docx");Files.createDirectories(output.getParent());Files.write(output,bytes);
    }
}
