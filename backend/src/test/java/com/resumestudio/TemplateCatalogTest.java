package com.resumestudio;

import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.TimeUnit;

/** Verify the actual build asset against the server definitions, including every text field. */
public final class TemplateCatalogTest {
    private static int checks;
    private static void verify(Path file) throws Exception {
        var actual=Json.array(Json.parse(Files.readString(file,StandardCharsets.UTF_8)));
        var expected=Templates.all();
        if(actual.size()!=expected.size()) throw new AssertionError("Catalogue template count differs from backend");
        var ids=new HashSet<String>();
        for(int i=0;i<expected.size();i++) {
            var item=Json.object(actual.get(i));
            if(!ids.add(Json.str(item,"id"))||!item.keySet().equals(expected.get(i).keySet())) throw new AssertionError("Catalogue keys or identifiers differ");
            for(var field:expected.get(i).entrySet()) {
                if(!Json.write(item.get(field.getKey())).equals(Json.write(field.getValue()))) throw new AssertionError("Catalogue text/data corrupted: "+field.getKey()+" in "+Json.str(item,"id"));
                checks++;
            }
        }
    }
    public static void main(String[] args) throws Exception {
        Path root=Files.createTempDirectory("resume-catalog-");
        Path generated=root.resolve("模板目录.json");
        String java=Path.of(System.getProperty("java.home"),"bin","java.exe").toString();
        var child=new ProcessBuilder(java,"-Dstdout.encoding=windows-1252","-Dstderr.encoding=windows-1252","-cp",System.getProperty("java.class.path"),"com.resumestudio.TemplateCatalog",generated.toString()).redirectErrorStream(true).redirectOutput(root.resolve("console.txt").toFile()).start();
        if(!child.waitFor(15,TimeUnit.SECONDS)||child.exitValue()!=0) throw new AssertionError("Catalogue generation failed under a non-Chinese console encoding");
        verify(generated);
        if(args.length>0) verify(Path.of(args[0]));
        // A valid JSON file can still contain irreversible question marks: reject it.
        Path bad=root.resolve("corrupt.json");
        Files.writeString(bad,Files.readString(generated).replace("后端简历","????"),StandardCharsets.UTF_8);
        boolean rejected=false;
        try {verify(bad);} catch(AssertionError expected) {rejected=true;}
        if(!rejected) throw new AssertionError("Question-mark catalogue corruption was accepted");
        System.out.println("PASS: "+checks+" template catalogue field checks, non-Chinese console and corrupted asset rejection");
    }
}
