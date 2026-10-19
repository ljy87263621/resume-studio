package com.resumestudio;

import java.text.Normalizer;
import java.util.*;
import java.util.regex.*;

/** Local, deterministic resume mapping shared by desktop and Android; never executes document content. */
public final class ResumeImport {
    private ResumeImport() {}
    public static final int MAX_TEXT=100000;
    private static final Pattern EMAIL=Pattern.compile("[A-Za-z0-9._%+\\-]+@[A-Za-z0-9.\\-]+\\.[A-Za-z]{2,}");
    private static final Pattern PHONE=Pattern.compile("(?<!\\d)(?:\\+?86[ -]?)?1[3-9](?:[ -]?\\d){9}(?!\\d)");
    private static final LinkedHashMap<String,List<String>> HEADINGS=new LinkedHashMap<>();
    static {
        HEADINGS.put("教育经历",List.of("教育经历","教育背景","学习经历","学历信息","education","educational background"));
        HEADINGS.put("专业技能",List.of("专业技能","个人技能","技能特长","技术能力","技能清单","技术栈","technical skills","skills"));
        HEADINGS.put("项目经历",List.of("项目经历","项目经验","项目实践","个人项目","projects","project experience"));
        HEADINGS.put("实习经历",List.of("实习经历","实习经验","实习实践","internship","internships","internship experience"));
        HEADINGS.put("工作经历",List.of("工作经历","工作经验","职业经历","任职经历","work experience","employment","professional experience"));
        HEADINGS.put("校园经历",List.of("校园经历","校园实践","社团经历","学生工作","活动经历","campus experience"));
        HEADINGS.put("荣誉证书",List.of("荣誉证书","荣誉奖项","获奖情况","获奖经历","奖项证书","证书","奖项","certifications","certificates","awards","honors"));
        HEADINGS.put("个人简介",List.of("个人简介","自我评价","自我介绍","个人评价","个人优势","个人总结","summary","profile","about me","personal statement"));
        HEADINGS.put("研究经历",List.of("研究经历","科研经历","论文发表","research","publications"));
        HEADINGS.put("开源贡献",List.of("开源贡献","开源项目","open source"));
        HEADINGS.put("其他资料",List.of("其他资料","其他信息","补充信息","additional information"));
    }
    public static String category(String title) {
        String clean=Normalizer.normalize(title,Normalizer.Form.NFKC).trim().replaceAll("^[#\\d一二三四五六七八九十、.．()（）\\s]+","").replaceAll("[：:|丨·•]+$","").toLowerCase(Locale.ROOT);
        String compact=clean.replaceAll("\\s+","");
        for(var entry:HEADINGS.entrySet())for(String alias:entry.getValue())if(compact.equals(alias.replace(" ","")))return entry.getKey();
        return "";
    }
    public static Map<String,Object> parse(Map<String,Object> request) {
        String text=Json.str(request,"text");if(text.length()>MAX_TEXT)throw new IllegalArgumentException("PDF 文字超过 10 万字符，请拆分后导入");
        if(text.trim().isEmpty())throw new IllegalArgumentException("未识别到可用文字，请选择更清晰的 PDF 或检查 OCR 语言");
        String source=Json.str(request,"sourceName");if(source.length()>300)source=source.substring(0,300);
        var profile=new LinkedHashMap<String,Object>();for(String key:List.of("name","role","phone","email","city","link","summary"))profile.put(key,"");
        List<Object> sections=new ArrayList<>();List<String> warnings=new ArrayList<>();String current="";StringBuilder content=new StringBuilder(),unassigned=new StringBuilder();
        int lineIndex=0;String pendingField="";
        for(String raw:text.replace("\r\n","\n").replace('\r','\n').replace('\f','\n').split("\n")){
            String line=Normalizer.normalize(raw,Normalizer.Form.NFKC).replaceAll("[\\p{Cc}&&[^\\t]]","").trim().replaceAll("(?<=\\p{IsHan}) +(?=\\p{IsHan})","");
            line=line.replaceFirst("^(姓名|求职意向|应聘岗位|电话|手机|邮箱|城市)[,，.．]\\s*","$1:");
            if(line.isEmpty()){if(content.length()>0)content.append('\n');continue;}
            if(line.matches("(?i)^(?:第\\s*)?\\d+\\s*(?:/|of|页[ /共]*)\\s*\\d+\\s*页?$|^page\\s+\\d+(?:\\s+of\\s+\\d+)?$"))continue;
            String title=category(line);
            String inline="";
            if(title.isEmpty()){
                int colon=line.indexOf(':');if(colon<0)colon=line.indexOf('：');
                if(colon>0){title=category(line.substring(0,colon));if(!title.isEmpty())inline=line.substring(colon+1).trim();}
            }
            if(!title.isEmpty()){
                pendingField="";
                flush(sections,profile,current,content);current=title;if(!inline.isEmpty())content.append(inline).append('\n');continue;
            }
            if(!pendingField.isEmpty()){profile.put(pendingField,line);pendingField="";continue;}
            String bare=line.replaceAll("[\\s:：]+","").toLowerCase(Locale.ROOT);
            if(List.of("姓名","name","求职意向","目标岗位","应聘岗位","objective").contains(bare)){pendingField=bare.equals("姓名")||bare.equals("name")?"name":"role";continue;}
            if(line.replaceAll("\\s+","").matches("个人信息[:：]?|基本信息[:：]?|联系方式[:：]?|(?i:contact|personalinformation)[:：]?"))continue;
            // Only header/contact-area text becomes profile fields; experience body is retained verbatim.
            boolean contact=current.isEmpty();boolean consumed=false;
            if(contact){
                Matcher email=EMAIL.matcher(line);if(email.find()&&Json.str(profile,"email").isEmpty())profile.put("email",email.group());
                Matcher phone=PHONE.matcher(line);if(phone.find()&&Json.str(profile,"phone").isEmpty())profile.put("phone",phone.group().replace(" ","").replace("-",""));
                String labeledPhone=label(line,"联系电话|电话|手机|手机号|phone|mobile|tel");if(Json.str(profile,"phone").isEmpty()&&labeledPhone.matches("[+\\d(). -]{7,28}")){String digits=labeledPhone.replaceAll("\\D","");if(digits.length()>=7&&digits.length()<=15)profile.put("phone",labeledPhone);}
                String value=label(line,"姓\\s*名|name");if(!value.isEmpty()){if(Json.str(profile,"name").isEmpty())profile.put("name",value);consumed=true;}
                value=label(line,"求职意向|应聘岗位|目标岗位|求职岗位|意向岗位|职位|objective|desired position");if(!value.isEmpty()){if(Json.str(profile,"role").isEmpty())profile.put("role",value);consumed=true;}
                value=label(line,"现居地|居住地|所在城市|城市|地址|location|city");if(!value.isEmpty()){if(Json.str(profile,"city").isEmpty())profile.put("city",value);consumed=true;}
                Matcher link=Pattern.compile("https?://[^\\s|丨<>]+",Pattern.CASE_INSENSITIVE).matcher(line);if(link.find()&&Json.str(profile,"link").isEmpty())profile.put("link",link.group());
                if(Json.str(profile,"name").isEmpty()&&lineIndex<5&&line.matches("[\\p{IsHan}]{2,5}")&&!line.matches(".*(?:简历|信息|岗位|经历|技能|工程师|求职|应聘).*")){profile.put("name",line);consumed=true;}
                // Preserve mixed contact lines in unassigned text rather than silently losing other details.
                if(!consumed){if(unassigned.length()>0)unassigned.append('\n');unassigned.append(line);}
            }else content.append(line).append('\n');
            lineIndex++;
        }
        flush(sections,profile,current,content);
        String rest=unassigned.toString().trim();if(!rest.isEmpty())sections.add(section("其他资料",rest));
        if(sections.isEmpty())warnings.add("未识别到经历模块，请在预览中核对原文并补充模块名称。");
        if(Json.str(profile,"name").isEmpty())warnings.add("姓名未可靠识别，请手动填写。");
        if(Json.str(profile,"phone").isEmpty()&&Json.str(profile,"email").isEmpty())warnings.add("未识别到电话或邮箱，请核对联系方式。");
        if(((Number)request.getOrDefault("ocrPages",0)).intValue()>0)warnings.add("部分页面使用本地 OCR，姓名、数字和日期可能有误，请核对后确认。");
        if(!rest.isEmpty())warnings.add("未归类的原文保留在“其他资料”，可以调整归属或删除重复联系方式。");
        return Map.of("sourceName",source,"profile",profile,"sections",sections,"warnings",warnings,"pageCount",request.getOrDefault("pageCount",1),"ocrPages",request.getOrDefault("ocrPages",0));
    }
    private static String label(String line,String expression){Matcher m=Pattern.compile("(?i)^(?:"+expression+")\\s*[:：|丨]\\s*(.+)$").matcher(line);return m.matches()?m.group(1).split("[|丨]",2)[0].trim():"";}
    private static Map<String,Object> section(String title,String content){if(content.length()>30000)throw new IllegalArgumentException("单个模块超过 3 万字符，请拆分 PDF");return new LinkedHashMap<>(Map.of("title",title,"content",content,"visible",true));}
    private static void flush(List<Object> sections,Map<String,Object> profile,String title,StringBuilder content){
        String value=content.toString().trim();content.setLength(0);if(value.isEmpty()||title.isEmpty())return;
        if(title.equals("个人简介")){String previous=Json.str(profile,"summary");profile.put("summary",previous.isEmpty()?value:previous+"\n"+value);}else sections.add(section(title,value));
    }
    public static Map<String,Object> create(Map<String,Object> request) {
        String templateId=Json.str(request,"templateId");var template=Templates.all().stream().map(Json::object).filter(t->templateId.equals(Json.str(t,"id"))).findFirst().orElseThrow(()->new IllegalArgumentException("请选择有效的简历模板"));
        var draft=Json.object(request.get("draft"));var profile=new LinkedHashMap<>(Json.object(draft.get("profile")));var imported=Json.array(draft.get("sections"));
        List<Object> sections=new ArrayList<>();Set<Integer> used=new HashSet<>();
        for(Object item:Json.array(template.get("sections"))){String title=item.toString(),canonical=category(title);StringBuilder value=new StringBuilder();for(int i=0;i<imported.size();i++){var s=Json.object(imported.get(i));if(!canonical.isEmpty()&&canonical.equals(category(Json.str(s,"title")))){if(value.length()>0)value.append("\n\n");value.append(Json.str(s,"content"));used.add(i);}}sections.add(section(title,value.toString()));}
        for(int i=0;i<imported.size();i++)if(!used.contains(i)){var s=Json.object(imported.get(i));sections.add(section(Json.str(s,"title"),Json.str(s,"content")));}
        String title=Json.str(request,"title").trim();if(title.isEmpty())title=Json.str(profile,"name").isEmpty()?"PDF 导入简历":Json.str(profile,"name")+"的简历";
        return new LinkedHashMap<>(Map.of("id",UUID.randomUUID().toString(),"title",title,"templateId",templateId,"profile",profile,"sections",sections,"color",Json.str(template,"color"),"fontFamily","MiSans","fontSize",Json.str(template,"origin").equals("github")?11:12,"lineHeight",Json.str(template,"origin").equals("github")?1.45:1.6));
    }
}
