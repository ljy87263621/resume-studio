package com.resumestudio;

import java.io.*;
import java.nio.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;
import javax.xml.parsers.*;
import org.w3c.dom.*;
import com.tom_roush.pdfbox.pdmodel.PDDocument;
import com.tom_roush.pdfbox.text.PDFTextStripper;

final class MobileImport {
    static Map<String,Object> read(String name,byte[] bytes)throws Exception {
        List<Object> blocks=new ArrayList<>();String lower=name.toLowerCase(Locale.ROOT);
        if(lower.endsWith(".docx")){
            Map<String,byte[]> xml=new HashMap<>();try(ZipInputStream zip=new ZipInputStream(new ByteArrayInputStream(bytes))){ZipEntry e;while((e=zip.getNextEntry())!=null){if(e.getName().equals("word/document.xml")||e.getName().equals("word/styles.xml")){byte[] content=MobileCompat.readNBytes(zip,20*1024*1024+1);if(content.length>20*1024*1024)throw new IOException("Word 展开内容过大");xml.put(e.getName(),content);}}}
            if(!xml.containsKey("word/document.xml"))throw new IOException("DOCX 格式无效");
            Map<String,Element> styles=new HashMap<>();if(xml.containsKey("word/styles.xml")){NodeList nodes=parse(xml.get("word/styles.xml")).getElementsByTagNameNS("*","style");for(int i=0;i<nodes.getLength();i++){Element s=(Element)nodes.item(i);styles.put(s.getAttribute("w:styleId"),s);}}
            NodeList paragraphs=parse(xml.get("word/document.xml")).getElementsByTagNameNS("*","p");
            for(int i=0;i<paragraphs.getLength();i++){Element p=(Element)paragraphs.item(i);StringBuilder text=new StringBuilder();collect(p,text);int level=outline(p);if(level==0){String style=attr(p,"pStyle","w:val");Set<String> seen=new HashSet<>();while(level==0&&styles.containsKey(style)&&seen.add(style)){Element s=styles.get(style);level=outline(s);Matcher m=Pattern.compile("(?i)(?:heading|标题)\\s*([1-6])").matcher(style+" "+attr(s,"name","w:val"));if(level==0&&m.find())level=Integer.parseInt(m.group(1));style=attr(s,"basedOn","w:val");}}blocks.add(MobileCompat.map("text",text.toString(),"level",text.toString().trim().isEmpty()?0:level));}
        }else if(lower.endsWith(".pdf")){
            try(PDDocument pdf=PDDocument.load(bytes)){
                if(pdf.getNumberOfPages()>200)throw new IOException("PDF 超过 200 页，请拆分后导入");
                PDFTextStripper stripper=new PDFTextStripper();stripper.setSortByPosition(true);StringBuilder all=new StringBuilder();
                for(int page=1;page<=pdf.getNumberOfPages();page++){stripper.setStartPage(page);stripper.setEndPage(page);all.append(stripper.getText(pdf)).append('\n');if(all.length()>500000)throw new IOException("文字超过 50 万字符");}blocks=text(all.toString());
            }
        }else if(lower.endsWith(".md")||lower.endsWith(".markdown")||lower.endsWith(".txt")){
            String text;if(bytes.length>=2&&((bytes[0]==(byte)255&&bytes[1]==(byte)254)||(bytes[0]==(byte)254&&bytes[1]==(byte)255)))text=new String(bytes,Charset.forName("UTF-16"));
            else try{text=StandardCharsets.UTF_8.newDecoder().decode(ByteBuffer.wrap(bytes)).toString();}catch(CharacterCodingException e){text=new String(bytes,Charset.forName("GB18030"));}blocks=text(text.replace("\uFEFF",""));
        }else throw new IOException("请选择 Markdown、TXT、DOCX 或文字型 PDF");
        int length=blocks.stream().map(Json::object).mapToInt(b->Json.str(b,"text").length()).sum();if(length==0)throw new IOException("没有可导入的文字，扫描 PDF 请先做 OCR");if(length>500000)throw new IOException("文字超过 50 万字符，请拆分后导入");
        return MobileCompat.map("sourceName",name,"blocks",blocks);
    }
    private static List<Object> text(String text){
        List<Object> blocks=new ArrayList<>();String fence="";
        for(String line:text.replace("\r\n","\n").replace('\r','\n').split("\n",-1)){
            String trimmed=line.trim();if(trimmed.startsWith("```")||trimmed.startsWith("~~~")){String marker=trimmed.substring(0,3);if(fence.isEmpty())fence=marker;else if(fence.equals(marker))fence="";blocks.add(MobileCompat.map("text",line,"level",0));continue;}
            Matcher m=Pattern.compile("^\\s*(#{1,6})\\s+(.+?)\\s*#*\\s*$").matcher(line);
            if(fence.isEmpty()&&m.matches())blocks.add(MobileCompat.map("text",m.group(2),"level",m.group(1).length()));
            else if(fence.isEmpty()&&trimmed.matches("={3,}|-{3,}")&&!blocks.isEmpty()&&!Json.str(Json.object(blocks.get(blocks.size()-1)),"text").trim().isEmpty()){var previous=new LinkedHashMap<>(Json.object(blocks.get(blocks.size()-1)));previous.put("level",trimmed.charAt(0)=='='?1:2);blocks.set(blocks.size()-1,previous);}
            else blocks.add(MobileCompat.map("text",line,"level",0));
        }
        boolean headings=blocks.stream().map(Json::object).anyMatch(b->((Number)b.get("level")).intValue()>0);
        if(!headings)for(int i=0;i<blocks.size();i++){Map<String,Object>b=Json.object(blocks.get(i));String t=Json.str(b,"text").trim();if(t.length()<=300&&t.matches("(?i)(?:(?:\\d{1,3}[.、．)）:]|[（(]\\d{1,3}[)）]|Q\\s*\\d+|第.+题).+|.+[？?])"))blocks.set(i,new LinkedHashMap<>(MobileCompat.map("text",Json.str(b,"text"),"level",1)));}
        return blocks;
    }
    private static Document parse(byte[] bytes)throws Exception {
        // Android's Harmony parser does not implement Xerces' disallow-doctype feature.
        // DOCX needs no DTD: reject its declaration, including UTF-16/32 byte layouts,
        // and deny every external resolution instead of relying on a vendor feature.
        String markup=new String(bytes,StandardCharsets.ISO_8859_1).replace("\u0000","");
        if(Pattern.compile("(?i)<!\\s*(DOCTYPE|ENTITY)\\b").matcher(markup).find())throw new IOException("Word XML 不允许 DTD 或实体声明");
        DocumentBuilderFactory f=DocumentBuilderFactory.newInstance();f.setNamespaceAware(true);f.setExpandEntityReferences(false);
        DocumentBuilder builder=f.newDocumentBuilder();builder.setEntityResolver((publicId,systemId)->{throw new org.xml.sax.SAXException("不允许外部 XML 资源");});return builder.parse(new ByteArrayInputStream(bytes));
    }
    private static String attr(Element e,String tag,String name){NodeList n=e.getElementsByTagNameNS("*",tag);return n.getLength()==0?"":((Element)n.item(0)).getAttribute(name);}
    private static int outline(Element e){try{int n=Integer.parseInt(attr(e,"outlineLvl","w:val"));return n>=0&&n<=5?n+1:0;}catch(NumberFormatException ex){return 0;}}
    private static void collect(Node n,StringBuilder b){if(n instanceof Element){String name=n.getLocalName();if("t".equals(name)){b.append(n.getTextContent());return;}if("tab".equals(name))b.append('\t');if("br".equals(name))b.append('\n');}for(Node c=n.getFirstChild();c!=null;c=c.getNextSibling())collect(c,b);}
}
