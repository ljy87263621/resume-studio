package com.resumestudio;
import java.nio.file.*;
import java.util.*;

public final class ResumeImportTest {
    private static int count;
    private static void check(boolean condition,String label){if(!condition)throw new AssertionError(label);count++;System.out.println("PASS: "+label);}
    public static void main(String[] args)throws Exception {
        String text="个人简历\n姓名：张三\n求职意向：嵌入式软件工程师\n电话：13800138000\n邮箱：person@example.com\n城市：深圳\n教育背景\n2023 - 2027 测试大学 本科\n专业技能：C/C++、Linux\n项目经历\n传感器网关\n负责 UART 与 TCP 协议调试\n自我评价\n严谨认真\n开源贡献\n提交补丁";
        var draft=ResumeImport.parse(Map.of("text",text,"sourceName","resume.pdf"));var profile=Json.object(draft.get("profile"));
        check(Json.str(profile,"name").equals("张三")&&Json.str(profile,"phone").equals("13800138000")&&Json.str(profile,"email").equals("person@example.com"),"Chinese contact fields map without inventing claims");
        check(Json.str(profile,"summary").equals("严谨认真"),"self evaluation maps to the profile summary");
        var created=ResumeImport.create(Map.of("templateId","gh-embedded-billryan","draft",draft));Server.validate("resumes",created);
        var sections=Json.array(created.get("sections"));check(sections.stream().map(Json::object).anyMatch(s->Json.str(s,"title").equals("教育背景")&&Json.str(s,"content").contains("测试大学")),"education aliases fill the selected template module");
        check(sections.stream().map(Json::object).anyMatch(s->Json.str(s,"title").equals("开源贡献")&&Json.str(s,"content").contains("补丁")),"sections missing from the template are appended without loss");
        var another=ResumeImport.create(Map.of("templateId","campus","draft",draft));check(!created.get("id").equals(another.get("id")),"each confirmation creates a separate resume ID");
        var unknown=ResumeImport.parse(Map.of("text","姓名：李四\n个人作品演示链接\n无法分类但需要保留的事实内容"));check(Json.array(unknown.get("sections")).stream().map(Json::object).anyMatch(s->Json.str(s,"content").contains("事实内容")),"unclassified content is retained for review");
        var english=ResumeImport.parse(Map.of("text","Name: Alex Smith\nEmail: alex@example.com\nEDUCATION\nExample University\nPROJECTS\nDevice project\nSKILLS\nC and Linux"));check(Json.str(Json.object(english.get("profile")),"name").equals("Alex Smith")&&Json.array(english.get("sections")).size()==4,"English labels and section headings map");
        var ocr=ResumeImport.parse(Map.of("text","姓 名 ： 张 三\n电 话 ： 13800138000\n专 业 技 能\nC/C++\n项目经历\nUART 调试","ocrPages",1));check(Json.str(Json.object(ocr.get("profile")),"name").equals("张三")&&!Json.array(ocr.get("warnings")).isEmpty(),"OCR character spacing is normalized and review warnings are explicit");
        var split=ResumeImport.parse(Map.of("text","姓名\n王五\n求职意向\n嵌入式实习\nPhone: +1 202-555-0189\nSkills\nC"));check(Json.str(Json.object(split.get("profile")),"role").equals("嵌入式实习")&&Json.str(Json.object(split.get("profile")),"phone").contains("202"),"separate-line labels and international phone fields map");
        try{ResumeImport.parse(Map.of("text"," "));throw new AssertionError("Empty PDF accepted");}catch(IllegalArgumentException expected){count++;}
        Path root=Files.createTempDirectory("resume-import-test");Store store=new Store(root);store.save("resumes",created);store.save("resumes",another);check(store.list("resumes").size()==2,"creating an imported resume preserves existing records");
        if(args.length>0){for(String file:List.of("text","scan","mixed","columns")){var input=Json.object(Json.parse(Files.readString(Paths.get(args[0],file+"-desktop.json"))));var result=ResumeImport.parse(input);check(Json.str(Json.object(result.get("profile")),"phone").contains("13800138000")&&Json.array(result.get("sections")).stream().map(Json::object).anyMatch(s->Json.str(s,"title").equals("项目经历")),file+": actual desktop PDF/OCR output maps contact and project sections");if(file.equals("columns"))check(Json.array(result.get("sections")).stream().map(Json::object).anyMatch(s->Json.str(s,"title").equals("专业技能")&&Json.str(s,"content").contains("Linux")&&!Json.str(s,"content").contains("UART")),"two-column PDF keeps skills and project content in separate modules");}}
        if(args.length>0){var scan=ResumeImport.parse(Json.object(Json.parse(Files.readString(Paths.get(args[0],"scan-desktop.json")))));check(Json.array(scan.get("sections")).stream().map(Json::object).anyMatch(s->Json.str(s,"title").equals("专业技能")&&Json.str(s,"content").contains("Linux")&&Json.str(s,"content").contains("FreeRTOS")),"mixed-script OCR lines stay with their original skills module");}
        System.out.println("PASS: "+count+" resume import mapping and persistence checks");
    }
}
