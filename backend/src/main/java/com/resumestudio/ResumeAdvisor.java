package com.resumestudio;

import java.util.*;
import java.util.regex.Pattern;

/** Rule ideas ported from ljy87263621/resume-helper (MIT); adapted to this app's section model. */
public final class ResumeAdvisor {
    private static boolean contains(String value,String regex) { return Pattern.compile(regex,Pattern.CASE_INSENSITIVE).matcher(value).find(); }
    public static Map<String,Object> check(Map<String,Object> resume) {
        var profile=Json.object(resume.get("profile")); var modules=new ArrayList<Map<String,Object>>();
        var basic=new ArrayList<String>();
        if(Json.str(profile,"name").isBlank()) basic.add("填写姓名，便于招聘方识别。");
        if(Json.str(profile,"phone").isBlank()) basic.add("补充联系电话。");
        if(!Json.str(profile,"email").matches("[^\\s@]+@[^\\s@]+\\.[^\\s@]+")) basic.add("检查邮箱格式。");
        if(Json.str(profile,"role").isBlank()) basic.add("填写明确的求职意向。");
        modules.add(Map.of("title","基本信息","status",basic.isEmpty()?"已填":"待优化","suggestions",basic));
        String category=Templates.all().stream().filter(t->Json.str(t,"id").equals(Json.str(resume,"templateId"))).map(t->Json.str(t,"category")).findFirst().orElse("通用型");
        for(Object item:Json.array(resume.get("sections"))) {
            var section=Json.object(item); String title=Json.str(section,"title"),text=Json.str(section,"content"); var tips=new ArrayList<String>();
            String status;
            if(Boolean.FALSE.equals(section.get("visible"))) status="已隐藏";
            else if(text.isBlank()) { status="未填"; if(!contains(title,"荣誉|证书|评价|个人作品")) tips.add("补充与目标岗位相关的真实内容；没有相关经历时可隐藏此模块。"); }
            else {
                if(contains(text,"\\[填写|待补充|示例大学|示例姓名|项目名称示例")) tips.add("替换示例和占位内容。");
                if(contains(title,"教育")) {
                    if(!contains(text,"本科|硕士|博士|专科|学士|Bachelor|Master")) tips.add("补充专业与学历。");
                    if(!contains(text,"20\\d{2}")) tips.add("补充教育经历的起止时间。");
                }
                if(contains(title,"技能")) {
                    var lines=Arrays.stream(text.split("\\r?\\n")).filter(s->!s.isBlank()).toList();
                    if(lines.size()>8) tips.add("技能条目较多，优先保留与目标岗位相关的 3–6 组能力。");
                    if(lines.stream().anyMatch(s->s.length()>90)) tips.add("技能描述较长，按技术方向拆成短句。");
                }
                if(contains(title,"项目|经历|实践")) {
                    if(!text.contains("## ")) tips.add("用小标题说明项目 / 单位名称与个人角色。");
                    if(!contains(text,"负责|设计|实现|开发|调试|优化|搭建|维护|验证|develop|implement")) tips.add("说明个人承担的动作与职责，避免只罗列技术名词。");
                    if(!contains(text,"\\d+\\s*(%|ms|MB|KB|fps|秒|毫秒|个|项|次)|测试|验证|上线|交付|故障|稳定|完成")) tips.add("补充可核实的结果或验证方式。没有测量数据时写验证结论，不要编造指标。");
                    if(category.equals("嵌入式开发")&&!contains(text,"STM32|ARM|MCU|RTOS|FreeRTOS|Linux|RK\\d|ESP32|芯片|开发板|单片机|平台")) tips.add("嵌入式经历建议明确硬件平台、系统环境与调试 / 验证方法。");
                    if(category.equals("前端开发")&&!contains(text,"Vue|React|TypeScript|JavaScript|uni-app|HTML|CSS|WinUI|框架")) tips.add("前端经历建议说明框架、页面 / 组件职责、兼容性或性能验证。");
                    if(category.equals("后端开发")&&!contains(text,"Java|Spring|Go|Python|SQL|Redis|接口|数据库|服务|协议")) tips.add("后端经历建议明确技术栈、接口 / 数据模型与可靠性验证。");
                }
                if(contains(title,"评价")&&(text.length()>160||contains(text,"认真负责|团队合作精神"))) tips.add("压缩泛化描述，用真实技术方向与经历说明优势。");
                status=tips.isEmpty()?"已填":"待优化";
            }
            modules.add(Map.of("title",title,"status",status,"suggestions",tips));
        }
        return Map.of("modules",modules,"tips",List.of("这些是本地规则建议，未使用在线 AI，也不代表招聘平台评分。","数字和结论必须有真实依据；暂未测量的结果可写验证步骤。","A4 容量是估算，最终分页以导出的 PDF 为准。"));
    }
}
