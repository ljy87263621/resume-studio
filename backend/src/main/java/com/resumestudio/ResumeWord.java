package com.resumestudio;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;

/** Native editable Word blocks and embedded images, requiring only the bundled java.base. */
final class ResumeWord {
    private final Map<String,Object> resume;
    private final List<Media> media=new ArrayList<>();
    private record Media(String path,byte[] bytes){}
    ResumeWord(Map<String,Object> resume){this.resume=resume;}
    private static String escape(String value){return value.replace("&","&amp;").replace("<","&lt;").replace(">","&gt;").replace("\"","&quot;").replaceAll("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]","");}
    private String runs(String text,boolean bold,int heading){
        String font=Json.str(resume,"fontFamily");if(!Set.of("MiSans","Microsoft YaHei","SimSun","Arial","Times New Roman").contains(font))font="MiSans";
        double size=heading==1?24:heading==2?14:heading>2?12:ResumeLayout.number(resume,"fontSize",12,10,16);
        String properties="<w:rFonts w:ascii=\""+font+"\" w:hAnsi=\""+font+"\" w:eastAsia=\""+font+"\"/><w:sz w:val=\""+Math.round(size*2)+"\"/>"+(bold?"<w:b/>":"");
        return "<w:r><w:rPr>"+properties+"</w:rPr><w:t xml:space=\"preserve\">"+escape(text).replace("\n","</w:t><w:br/><w:t xml:space=\"preserve\">")+"</w:t></w:r>";
    }
    private String paragraph(String text,int heading,boolean center){
        StringBuilder result=new StringBuilder("<w:p><w:pPr><w:spacing w:after=\"80\" w:before=\"").append(heading>1?Math.round(ResumeLayout.number(resume,"sectionSpacing",4.8,0,12)*56.6929):0).append("\" w:line=\"").append(Math.round(ResumeLayout.number(resume,"lineHeight",1.6,1.2,2)*240)).append("\" w:lineRule=\"auto\"/>");
        if(heading>0)result.append("<w:keepNext/>");if(center)result.append("<w:jc w:val=\"center\"/>");result.append("</w:pPr>");
        Matcher matcher=Pattern.compile("\\*\\*(.+?)\\*\\*").matcher(text);int cursor=0;
        while(matcher.find()){result.append(runs(text.substring(cursor,matcher.start()),heading>0,heading)).append(runs(matcher.group(1),true,heading));cursor=matcher.end();}
        return result.append(runs(text.substring(cursor),heading>0,heading)).append("</w:p>").toString();
    }
    private String table(List<List<String>> rows){
        if(rows.isEmpty())return "";int columns=rows.stream().mapToInt(List::size).max().orElse(1);
        long width=Math.round(11906-2*ResumeLayout.number(resume,"pageMarginHorizontal",16,8,25)*56.6929),cellWidth=width/columns;
        StringBuilder result=new StringBuilder("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>");
        for(String edge:List.of("top","left","bottom","right","insideH","insideV"))result.append("<w:").append(edge).append(" w:val=\"single\" w:sz=\"4\" w:color=\"CCD3DD\"/>");
        result.append("</w:tblBorders></w:tblPr><w:tblGrid>");for(int i=0;i<columns;i++)result.append("<w:gridCol w:w=\"").append(cellWidth).append("\"/>");result.append("</w:tblGrid>");
        for(int r=0;r<rows.size();r++){
            result.append("<w:tr><w:trPr><w:cantSplit/>");if(r==0)result.append("<w:tblHeader/>");result.append("</w:trPr>");
            for(int c=0;c<columns;c++)result.append("<w:tc><w:tcPr><w:tcW w:w=\"").append(cellWidth).append("\" w:type=\"dxa\"/>").append(r==0?"<w:shd w:fill=\"F1F4F8\"/>":"").append("</w:tcPr>").append(paragraph(c<rows.get(r).size()?rows.get(r).get(c):"",0,false)).append("</w:tc>");result.append("</w:tr>");
        }return result.append("</w:tbl>").toString();
    }
    private static int[] dimensions(byte[] bytes){
        if(bytes.length>24&&bytes[0]==(byte)137&&bytes[1]==80)return new int[] {java.nio.ByteBuffer.wrap(bytes,16,4).getInt(),java.nio.ByteBuffer.wrap(bytes,20,4).getInt()};
        if(bytes.length>4&&bytes[0]==(byte)255&&bytes[1]==(byte)216){
            int offset=2;while(offset+9<bytes.length){if(bytes[offset]!=(byte)255)break;int marker=bytes[offset+1]&255;if(marker==216||marker==217){offset+=2;continue;}int length=((bytes[offset+2]&255)<<8)|(bytes[offset+3]&255);if(length<2||offset+length+2>bytes.length)break;
                if(Set.of(192,193,194,195,197,198,199,201,202,203,205,206,207).contains(marker))return new int[] {((bytes[offset+7]&255)<<8)|(bytes[offset+8]&255),((bytes[offset+5]&255)<<8)|(bytes[offset+6]&255)};offset+=length+2;
            }
        }return new int[] {640,427};
    }
    private String image(Map<String,Object> image){
        String data=Json.str(image,"dataUrl"),caption=Json.str(image,"caption");StringBuilder result=new StringBuilder();
        if(ResumeLayout.validImage(data)){
            byte[] bytes=Base64.getDecoder().decode(data.substring(data.indexOf(',')+1));String type=data.substring(11,data.indexOf(';')),extension=type.equals("jpeg")?"jpg":type;int id=media.size()+1;media.add(new Media("image"+id+"."+extension,bytes));
            int[] size=dimensions(bytes);double ratio=size[0]>0&&size[1]>0?(double)size[1]/size[0]:2d/3;
            long width=Math.min(Math.round((210-2*ResumeLayout.number(resume,"pageMarginHorizontal",16,8,25))*36000*ResumeLayout.number(image,"widthPercent",100,10,100)/100),Math.max(1,size[0])*9525L),height=Math.round(width*ratio),maxHeight=Math.round(ResumeLayout.number(image,"maxHeightMm",60,20,170)*36000);
            if(height>maxHeight){width=Math.round(width*(double)maxHeight/height);height=maxHeight;}
            result.append("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"").append(width).append("\" cy=\"").append(height).append("\"/><wp:docPr id=\"").append(id).append("\" name=\"图片").append(id).append("\" descr=\"").append(escape(Json.str(image,"alt"))).append("\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"图片\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rId").append(id).append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").append(width).append("\" cy=\"").append(height).append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
        }
        if(!caption.isBlank())result.append(paragraph(caption,0,true));return result.toString();
    }
    private String content(Map<String,Object> section){
        StringBuilder result=new StringBuilder();List<List<String>> rows=new ArrayList<>();
        for(String line:Json.str(section,"content").split("\\R",-1)) {
            if(ResumeLayout.tableLine(line)){var cells=ResumeLayout.cells(line);if(!cells.stream().allMatch(c->c.matches(":?-{3,}:?")))rows.add(cells);continue;}
            if(!rows.isEmpty()){result.append(table(rows));rows.clear();}
            var match=ResumeLayout.IMAGE.matcher(line.trim());
            if(match.matches()){for(Object value:Json.array(section.getOrDefault("images",List.of()))){var image=Json.object(value);if(Json.str(image,"id").equals(match.group(1))){result.append(image(image));break;}}continue;}
            int heading=line.startsWith("### ")?4:line.startsWith("## ")?3:0;
            result.append(paragraph(heading>0?line.substring(heading==4?4:3).replace(" | ","    "):line.startsWith("- ")?"• "+line.substring(2):line,heading,false));
        }if(!rows.isEmpty())result.append(table(rows));return result.toString();
    }
    private static void entry(ZipOutputStream zip,String path,byte[] bytes)throws IOException{zip.putNextEntry(new ZipEntry(path));zip.write(bytes);zip.closeEntry();}
    private static void entry(ZipOutputStream zip,String path,String text)throws IOException{entry(zip,path,text.getBytes(StandardCharsets.UTF_8));}
    byte[] build()throws IOException {
        var profile=Json.object(resume.get("profile"));boolean center=Json.str(resume,"headerAlignment").equals("center");StringBuilder body=new StringBuilder(paragraph(Json.str(profile,"name"),1,center));if(!Json.str(profile,"role").isBlank())body.append(paragraph(Json.str(profile,"role"),0,center));
        List<String> contacts=new ArrayList<>();for(String key:List.of("phone","email","city","link"))if(!Json.str(profile,key).isBlank())contacts.add(Json.str(profile,key));
        for(Object value:Json.array(profile.getOrDefault("items",List.of()))){var item=Json.object(value);if(!Json.str(item,"value").isBlank())contacts.add(Json.str(item,"emoji")+" "+Json.str(item,"label")+(Json.str(item,"label").isBlank()?"":"：")+Json.str(item,"value"));}
        if(!contacts.isEmpty())body.append(paragraph(String.join(" | ",contacts),0,center));if(!Json.str(profile,"summary").isBlank())body.append(paragraph(Json.str(profile,"summary"),0,false));
        for(Object value:Json.array(resume.get("sections"))){var section=Json.object(value);if(Boolean.FALSE.equals(section.get("visible"))||Json.str(section,"content").isBlank())continue;body.append(paragraph(Json.str(section,"title"),2,false)).append(content(section));}
        long vertical=Math.round(ResumeLayout.number(resume,"pageMarginVertical",14,8,25)*56.6929),horizontal=Math.round(ResumeLayout.number(resume,"pageMarginHorizontal",16,8,25)*56.6929);
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();try(ZipOutputStream zip=new ZipOutputStream(bytes)){
            entry(zip,"[Content_Types].xml","<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpg\" ContentType=\"image/jpeg\"/><Default Extension=\"webp\" ContentType=\"image/webp\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            entry(zip,"_rels/.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            entry(zip,"word/document.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>"+body+"<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\""+vertical+"\" w:right=\""+horizontal+"\" w:bottom=\""+vertical+"\" w:left=\""+horizontal+"\"/></w:sectPr></w:body></w:document>");
            StringBuilder relationships=new StringBuilder("<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");for(int i=0;i<media.size();i++){Media item=media.get(i);entry(zip,"word/media/"+item.path(),item.bytes());relationships.append("<Relationship Id=\"rId").append(i+1).append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/").append(item.path()).append("\"/>");}entry(zip,"word/_rels/document.xml.rels",relationships.append("</Relationships>").toString());
        }return bytes.toByteArray();
    }
}
