package com.resumestudio;

import java.util.*;

/** Match the same position across discovery and pasted links without changing its browser URL. */
final class JobWorkflow {
    private JobWorkflow() { }
    static boolean sameJob(Map<String,Object> job,Map<String,Object> saved,boolean application) {
        String id=Json.str(job,"id"), savedId=Json.str(saved,application?"jobId":"id");
        if(!id.isBlank()&&id.equals(savedId)) return true;
        try { return canonical(Json.str(job,"url")).equals(canonical(Json.str(saved,"url"))); }
        catch(IllegalArgumentException ex) { return false; }
    }
    private static String canonical(String url) {
        String normalized=JobCapture.canonical(url);var uri=java.net.URI.create(normalized);String host=uri.getHost();
        if(!(host.equals("zhipin.com")||host.endsWith(".zhipin.com")||host.equals("nowcoder.com")||host.endsWith(".nowcoder.com"))||uri.getRawQuery()==null)return normalized;
        var parts=new ArrayList<String>();
        for(String part:uri.getRawQuery().split("&"))if(!Set.of("channel","pagesource","deliversource").contains(part.split("=",2)[0].toLowerCase(Locale.ROOT)))parts.add(part);
        return normalized.substring(0,normalized.indexOf('?'))+(parts.isEmpty()?"":"?"+String.join("&",parts));
    }
}
