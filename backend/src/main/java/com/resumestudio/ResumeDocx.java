package com.resumestudio;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.zip.*;
/** Editable Word paragraphs, independent of the HTML/PDF template layout. */
public final class ResumeDocx {
    private ResumeDocx(){}
    private static String escape(String value){return value.replace("&","&amp;").replace("<","&lt;").replace(">","&gt;").replace("\"","&quot;").replaceAll("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]","");}
    private static String paragraph(String text,boolean heading){return "<w:p><w:pPr><w:spacing w:after=\"120\"/>"+(heading?"<w:keepNext/>":"")+"</w:pPr><w:r><w:rPr>"+(heading?"<w:b/><w:sz w:val=\"28\"/>":"<w:sz w:val=\"22\"/>")+"</w:rPr><w:t xml:space=\"preserve\">"+escape(text)+"</w:t></w:r></w:p>";}
    private static void entry(ZipOutputStream zip,String path,String text)throws IOException{zip.putNextEntry(new ZipEntry(path));zip.write(text.getBytes(StandardCharsets.UTF_8));zip.closeEntry();}
    public static byte[] create(Map<String,Object> resume)throws IOException{
        var profile=Json.object(resume.get("profile"));StringBuilder body=new StringBuilder();body.append(paragraph(Json.str(profile,"name"),true));body.append(paragraph(Json.str(profile,"role"),false));
        List<String> contacts=new ArrayList<>();for(String key:List.of("phone","email","city","link"))if(!Json.str(profile,key).isBlank())contacts.add(Json.str(profile,key));body.append(paragraph(String.join(" | ",contacts),false));
        if(!Json.str(profile,"summary").isBlank())body.append(paragraph(Json.str(profile,"summary"),false));
        for(Object value:Json.array(resume.get("sections"))){var section=Json.object(value);if(Boolean.FALSE.equals(section.get("visible"))||Json.str(section,"content").isBlank())continue;body.append(paragraph(Json.str(section,"title"),true));for(String line:Json.str(section,"content").split("\\R",-1))body.append(paragraph(line,false));}
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();try(ZipOutputStream zip=new ZipOutputStream(bytes)){
            entry(zip,"[Content_Types].xml","<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            entry(zip,"_rels/.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            entry(zip,"word/document.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"+body+"<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"850\" w:right=\"850\" w:bottom=\"850\" w:left=\"850\"/></w:sectPr></w:body></w:document>");
        }return bytes.toByteArray();
    }
}
