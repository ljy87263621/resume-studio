package com.resumestudio;

import java.net.URI;
import java.util.*;
import java.util.regex.*;

/** Bounded HTML text extraction for public employer pages without JobPosting metadata. */
final class JobPageText {
    private static final Pattern TOKENS=Pattern.compile("(?s)<!--.*?-->|<![^>]*>|</?([a-zA-Z][\\w:-]*)\\b((?:[^>\"']|\"[^\"]*\"|'[^']*')*)>|([^<]+)");
    private static final Pattern ATTRIBUTES=Pattern.compile("([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s/>]+))");
    private static final Set<String> VOID=Set.of("area","base","br","col","embed","hr","img","input","link","meta","param","source","track","wbr");
    private static final Set<String> IGNORE=Set.of("script","style","nav","footer","form","noscript","template","svg","button","select","textarea");
    private static final Pattern DUTIES=Pattern.compile("(?i)岗位职责|工作职责|职位职责|工作内容|职位描述|职责描述|任职资格|任职要求|岗位要求|职位要求|招聘要求|responsibilities|requirements|qualifications|what you(?: will|'ll) do");
    private static final class Node {
        final String tag; final Map<String,String> attributes; final List<Object> content=new ArrayList<>();
        Node(String name,Map<String,String> values){tag=name;attributes=values;}
        String identity(){return attributes.getOrDefault("class","")+" "+attributes.getOrDefault("id","");}
        boolean ignored(){return IGNORE.contains(tag)||attributes.containsKey("hidden")||"true".equalsIgnoreCase(attributes.get("aria-hidden"))||attributes.getOrDefault("style","").matches("(?is).*(display\\s*:\\s*none|visibility\\s*:\\s*hidden).*" );}
        String text(){var result=new StringBuilder();append(result);return normalize(result.toString());}
        void append(StringBuilder result){
            if(ignored())return;
            boolean block=tag.matches("main|article|section|div|p|li|ul|ol|h[1-6]|dt|dd|tr|br|hr");if(block)result.append('\n');
            for(Object part:content){if(part instanceof Node n)n.append(result);else result.append(decode(part.toString()));}
            if(block)result.append('\n');
        }
    }
    private record Document(Node root,List<Node> nodes) {}
    private static Document parse(String html){
        var root=new Node("root",Map.of());var nodes=new ArrayList<Node>();var stack=new ArrayDeque<Node>();stack.push(root);
        var tokens=TOKENS.matcher(html);
        int tokenCount=0;while(tokens.find()&&++tokenCount<=100000){
            if(tokens.group(1)==null){if(tokens.group(3)!=null)stack.peek().content.add(tokens.group(3));continue;}
            String tag=tokens.group(1).toLowerCase(Locale.ROOT);
            if(tokens.group().startsWith("</")){
                if(stack.stream().anyMatch(n->n.tag.equals(tag)))while(stack.size()>1){if(stack.pop().tag.equals(tag))break;}continue;
            }
            var attrs=new LinkedHashMap<String,String>();var values=ATTRIBUTES.matcher(tokens.group(2));
            while(values.find())attrs.put(values.group(1).toLowerCase(Locale.ROOT),decode(Objects.requireNonNullElse(values.group(2),Objects.requireNonNullElse(values.group(3),Objects.requireNonNullElse(values.group(4),"")))));
            if(Pattern.compile("(?i)(?:^|\\s)hidden(?:\\s|=|$)").matcher(tokens.group(2)).find())attrs.put("hidden","");
            if(tag.equals("header")&&stack.stream().noneMatch(n->n.tag.equals("main")||n.tag.equals("article")))attrs.put("hidden","");
            var node=new Node(tag,attrs);stack.peek().content.add(node);
            // Ignored ancestors stay in the parse tree but never enter the field candidate list.
            if(stack.stream().noneMatch(Node::ignored)&&!node.ignored())nodes.add(node);
            if(!VOID.contains(tag)&&!tokens.group().endsWith("/>")){if(stack.size()>=256)throw new IllegalArgumentException("网页嵌套过深，建议浏览并读取");stack.push(node);}
        }
        return new Document(root,nodes);
    }
    static Map<String,Object> fields(String html,URI base){
        var document=parse(html);var nodes=document.nodes();
        String title=nodes.stream().filter(n->n.tag.equals("h1")).map(Node::text).filter(s->!s.isBlank()).findFirst().orElse("");
        if(title.isBlank())title=nodes.stream().filter(n->n.tag.equals("h2")||"title".equals(n.attributes.get("itemprop"))).map(Node::text).filter(s->!s.isBlank()).findFirst().orElse("");
        String pageTitle=nodes.stream().filter(n->n.tag.equals("title")).map(Node::text).findFirst().orElse("");
        if(title.isBlank())title=pageTitle.split("\\s*[|｜—]\\s*|\\s+-\\s+",2)[0];
        if(title.isBlank()||title.matches("(?is).*(安全验证|人机验证|请稍候|访问受限|页面不存在|404|登录注册).*" )||title.matches("(?i)(?:\\d{4}年?\\s*)?(人才招聘|校园招聘|社会招聘|招聘职位|职位招聘|招聘岗位|加入我们|加入芯德|Careers|Jobs|Open positions|Join us)"))return Map.of();
        String body="";
        for(Node node:nodes){
            if(!(node.tag.equals("main")||node.tag.equals("article")||node.identity().matches("(?is).*(entry-content|job[-_]?(description|detail|content)|position[-_]?(detail|content)|recruit[-_]?(detail|content)).*")))continue;
            String text=node.text();var evidence=DUTIES.matcher(text);int count=0;while(evidence.find())count++;
            boolean careerPath=base.getPath().matches("(?i).*(job|career|recruit|position|join-us|vacanc).*" );
            if(text.length()<40||(count<2&&!(count==1&&careerPath&&text.length()>80)))continue;
            if(body.isBlank()||text.length()<body.length())body=text;
        }
        if(body.isBlank())return Map.of();
        var result=new LinkedHashMap<String,Object>();result.put("title",title);result.put("description",body.substring(0,Math.min(body.length(),30000)));
        for(var entry:Map.of("location","工作地点|工作地址|工作城市|办公地点|Location","salary","薪资范围|薪资待遇|薪资|工资|Salary","experience","工作经验|经验要求|Experience","company","招聘公司|公司名称|Hiring organization").entrySet()){
            String value=labeled(body,entry.getValue());if(!value.isBlank())result.put(entry.getKey(),value);
        }
        if(!result.containsKey("company")){
            for(Node node:nodes)if(node.tag.equals("meta")&&"og:site_name".equalsIgnoreCase(node.attributes.get("property"))){String name=node.attributes.getOrDefault("content","");if(companyName(name)){result.put("company",name);break;}}
            if(!result.containsKey("company")){String[] parts=pageTitle.split("\\s*[|｜—]\\s*|\\s*-\\s*");String last=parts[parts.length-1].trim();if(parts.length>1&&companyName(last))result.put("company",last);}
        }
        return result;
    }
    private static boolean companyName(String value){return value.length()<=120&&value.matches(".*(科技|公司|集团|银行|研究院|医院|大学|股份|有限公司)$")&&!value.matches(".*(招聘|职位|岗位).*" );}
    static String labeled(String text,String label){
        var match=Pattern.compile("(?im)^(?:"+label+")\\s*[：:]?\\s*(?:\\n\\s*)?([^\\n]+)$").matcher(text);
        if(!match.find())return "";String value=match.group(1).trim();return value.length()>200||value.endsWith("：")||value.endsWith(":")?"":value;
    }
    static String normalize(String text){return text.replace('\u00a0',' ').replaceAll("[\\t\\x0B\\f ]+"," ").replaceAll(" *\\n *","\n").replaceAll("\\n{3,}","\n\n").trim();}
    static String decode(String text){
        var matcher=Pattern.compile("&#(x[0-9a-fA-F]+|[0-9]+);").matcher(text);var result=new StringBuilder();
        while(matcher.find()){
            String value=matcher.group(1),replacement=matcher.group();try{int point=Integer.parseInt(value.startsWith("x")?value.substring(1):value,value.startsWith("x")?16:10);if(Character.isValidCodePoint(point))replacement=new String(Character.toChars(point));}catch(IllegalArgumentException ignored){}
            matcher.appendReplacement(result,Matcher.quoteReplacement(replacement));
        }matcher.appendTail(result);
        return result.toString().replace("&nbsp;"," ").replace("&quot;","\"").replace("&apos;","'").replace("&lt;","<").replace("&gt;",">").replace("&ndash;","–").replace("&mdash;","—").replace("&amp;","&");
    }
}
