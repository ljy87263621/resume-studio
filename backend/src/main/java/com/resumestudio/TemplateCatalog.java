package com.resumestudio;

import java.util.*;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/** Build-time catalogue; the desktop can show templates before its local server starts. */
public final class TemplateCatalog {
    private TemplateCatalog() {}
    public static void main(String[] args) throws IOException {
        if(args.length!=1) throw new IllegalArgumentException("Provide the output JSON file path");
        // Console encoding depends on the Windows build agent's locale. Write the
        // catalogue directly so PowerShell never decodes or re-encodes these bytes.
        Files.writeString(Path.of(args[0]),Json.write(canonical(Templates.all())),StandardCharsets.UTF_8);
    }
    private static Object canonical(Object value) {
        if(value instanceof Map<?,?> map) {
            var sorted=new TreeMap<String,Object>(); map.forEach((key,item)->sorted.put(key.toString(),canonical(item))); return sorted;
        }
        if(value instanceof List<?> list) return list.stream().map(TemplateCatalog::canonical).toList();
        return value;
    }
}
