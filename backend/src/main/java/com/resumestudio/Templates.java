package com.resumestudio;

import java.util.*;

public final class Templates {
    public static List<Map<String,Object>> all() {
        List<Map<String,Object>> result=new ArrayList<>();
        result.add(github("gh-front-billryan","billryan · 中文前端简历","前端开发","#222222","billryan","前端技能、作品链接和项目实践；沿用原模板居中姓名与横线分节。",List.of("教育背景","专业技能","项目经历","实习经历","个人作品"),"billryan/resume",11477,"MIT","resume.cls / resume.tex","eadc3955cf006254dc8f2419e971fbe3f5bab413"));
        result.add(github("gh-front-deedy","Deedy · 前端双栏简历","前端开发","#666666","deedy","沿用原模板 33% / 66% 非对称双栏，左侧技能教育，右侧实践经历。",List.of("教育背景","专业技能","个人作品","工作经历","项目经历","荣誉证书"),"deedy/Deedy-Resume",5066,"Apache-2.0","OpenFonts/deedy-resume-openfont.cls / deedy_resume-openfont.xtx","6d06ba7d8eee5fb8870bb00cb13a777f2083aa18"));
        result.add(github("gh-back-awesome","Awesome-CV · 后端简历","后端开发","#DC352F","awesome","沿用原模板居中抬头、强调色分节与紧凑经历列表；提供后端填写引导。",List.of("专业技能","工作经历","项目经历","教育背景","荣誉证书"),"posquit0/Awesome-CV",28694,"LPPL-1.3c","awesome-cv.cls / examples/resume.tex","6701180c71479588dae5d895c4a10a6a572a40a0"));
        result.add(github("gh-back-billryan","billryan · 中文后端简历","后端开发","#222222","billryan","中文单栏技术简历，适合 Java、Go、Python 与服务器开发经历。",List.of("专业技能","工作经历","项目经历","教育背景","荣誉证书"),"billryan/resume",11477,"MIT","resume.cls / resume.tex","eadc3955cf006254dc8f2419e971fbe3f5bab413"));
        result.add(github("gh-embedded-billryan","billryan · 嵌入式简历","嵌入式开发","#222222","billryan","移植通用技术模板版式，并增加 C/C++、MCU、RTOS、Linux 的填写引导。",List.of("教育背景","专业技能","项目经历","实习经历","荣誉证书"),"billryan/resume",11477,"MIT","resume.cls / resume.tex","eadc3955cf006254dc8f2419e971fbe3f5bab413"));
        result.add(github("gh-embedded-awesome","Awesome-CV · 嵌入式简历","嵌入式开发","#167D78","awesome","原模板的技术履历排版，突出硬件平台、调试方法与可核实成果。",List.of("专业技能","项目经历","工作经历","教育背景","荣誉证书"),"posquit0/Awesome-CV",28694,"LPPL-1.3c","awesome-cv.cls / examples/resume.tex","6701180c71479588dae5d895c4a10a6a572a40a0"));
        result.add(github("gh-general-jake","Jake’s Resume · 通用单栏","通用型","#222222","jake","移植原模板黑白单栏、紧凑条目和下划线分节；适合通用技术求职。",List.of("教育背景","工作经历","项目经历","专业技能","荣誉证书"),"jakegut/resume",2828,"MIT","resume.tex","3e64a933141a7cda3a328f0743f73a201628a4ba"));
        result.add(github("gh-general-deedy","Deedy · 通用双栏","通用型","#666666","deedy","原模板的非对称双栏结构；教育技能在左，经历与项目在右。",List.of("教育背景","专业技能","个人作品","工作经历","项目经历","荣誉证书"),"deedy/Deedy-Resume",5066,"Apache-2.0","OpenFonts/deedy-resume-openfont.cls / deedy_resume-openfont.xtx","6d06ba7d8eee5fb8870bb00cb13a777f2083aa18"));
        result.addAll(List.of(
            template("campus","校招 · 清晰起点","校招 实习 Java 后端 前端","#245AE8","classic","教育背景优先，突出专业技能与项目实践。",List.of("教育背景","专业技能","项目经历","实习经历","荣誉证书")),
            template("embedded","嵌入式 · 技术实践","嵌入式 C++ Linux 校招","#167D78","sidebar","适合呈现软硬件技术栈、开发环境和项目职责。",List.of("教育背景","专业技能","项目经历","实践经历","荣誉证书")),
            template("professional","社招 · 职业进阶","社招 Java 后端 Go","#353D59","classic","工作经历优先，以职责与成果呈现技术成长。",List.of("专业技能","工作经历","项目经历","教育背景","自我评价")),
            template("minimal","极简 · 留白表达","通用 大学生 产品 运营","#222222","minimal","黑白排版，适合简洁、正式的求职场景。",List.of("教育背景","工作经历","项目经历","专业技能","自我评价")),
            template("creative","前端 · 轻盈色彩","前端 设计 实习 校招","#7955B4","band","色块标题与清晰分区，突出产品与开发实践。",List.of("教育背景","专业技能","项目经历","实习经历","个人作品")),
            template("research","算法 · 研究积累","AI Python 算法 研究 大模型","#1E668A","classic","覆盖科研经历、论文与工程实践。",List.of("教育背景","研究方向","科研经历","项目经历","论文与荣誉"))));
        return result;
    }
    private static Map<String,Object> template(String id,String name,String tags,String color,String layout,String description,List<String> sections) {
        Map<String,Object> result=new LinkedHashMap<>(Map.of("id",id,"name",name,"tags",tags,"color",color,"layout",layout,"description",description,"sections",sections));
        result.put("category",id.equals("embedded")?"嵌入式开发":id.equals("creative")?"前端开发":Set.of("professional","research").contains(id)?"后端开发":"通用型"); result.put("origin","builtin");
        return result;
    }
    private static Map<String,Object> github(String id,String name,String category,String color,String layout,String description,List<String> sections,String repo,int stars,String license,String file,String commit) {
        var result=template(id,name,category+" 校招 实习 社招",color,layout,description,sections);
        result.put("category",category); result.put("origin","github"); result.put("sourceRepo",repo); result.put("sourceUrl","https://github.com/"+repo); result.put("sourceFile",file); result.put("sourceCommit",commit); result.put("stars",stars); result.put("starsCheckedAt","2026-10-12"); result.put("license",license);
        result.put("adaptation","从原仓库 LaTeX 模板移植到 HTML 排版；中文使用系统字体、A4 输出；岗位分类与填写引导为本应用新增，原仓库并非嵌入式专属模板。");
        result.put("sideTitles",layout.equals("deedy")?List.of("教育背景","专业技能","个人作品","荣誉证书"):List.of()); return result;
    }
}
