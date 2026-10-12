package com.resumestudio;

import java.util.*;

/** Build-time catalogue; the desktop can show templates before its local server starts. */
public final class TemplateCatalog {
    private TemplateCatalog() {}
    public static void main(String[] args) { System.out.println(Json.write(canonical(Templates.all()))); }
    private static Object canonical(Object value) {
        if(value instanceof Map<?,?> map) {
            var sorted=new TreeMap<String,Object>(); map.forEach((key,item)->sorted.put(key.toString(),canonical(item))); return sorted;
        }
        if(value instanceof List<?> list) return list.stream().map(TemplateCatalog::canonical).toList();
        return value;
    }
}
