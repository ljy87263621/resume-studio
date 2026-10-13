package com.resumestudio;

import java.io.*;
import java.nio.file.*;
import java.time.Instant;
import java.util.*;
import java.util.zip.*;

/** Bounded, compressed checkpoints; automatic checkpoints are spaced five minutes apart. */
final class ResumeHistory {
    private final Path root;
    ResumeHistory(Path data) { root=data.resolve("history"); }
    private Path directory(String id) {
        if(!id.matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("Invalid identifier");
        return root.resolve(id);
    }
    private List<Path> files(String id) throws IOException {
        Path dir=directory(id); if(!Files.exists(dir)) return List.of();
        try(var stream=Files.list(dir)) { return stream.filter(p->p.getFileName().toString().endsWith(".json.gz")).sorted(Comparator.reverseOrder()).toList(); }
    }
    private Map<String,Object> read(Path file) throws IOException {
        try(var in=new GZIPInputStream(Files.newInputStream(file))) {
            byte[] bytes=in.readNBytes(2_100_001);
            if(bytes.length>2_100_000) throw new IOException("History exceeds size limit");
            return Json.object(Json.parse(new String(bytes,java.nio.charset.StandardCharsets.UTF_8)));
        }
    }
    void capture(String id,Map<String,Object> resume,String label,boolean automatic) throws IOException {
        var existing=files(id);
        if(automatic) for(Path file:existing) {
            var checkpoint=read(file);
            if(Boolean.TRUE.equals(checkpoint.get("automatic"))) {
                if(Instant.parse(Json.str(checkpoint,"createdAt")).plusSeconds(300).isAfter(Instant.now())) return;
                break;
            }
        }
        String version=String.format(Locale.ROOT,"%019d",System.currentTimeMillis())+"-"+UUID.randomUUID();
        Path dir=directory(id); Files.createDirectories(dir);
        Path target=dir.resolve(version+".json.gz"),temp=dir.resolve(version+".tmp");
        var checkpoint=new LinkedHashMap<String,Object>(); checkpoint.put("id",version); checkpoint.put("createdAt",Instant.now().toString());
        checkpoint.put("label",label); checkpoint.put("automatic",automatic); checkpoint.put("resume",resume);
        try(var out=new GZIPOutputStream(Files.newOutputStream(temp))) { out.write(Json.write(checkpoint).getBytes(java.nio.charset.StandardCharsets.UTF_8)); }
        Files.move(temp,target,StandardCopyOption.REPLACE_EXISTING);
        var revisions=files(id); for(int i=20;i<revisions.size();i++) Files.delete(revisions.get(i));
    }
    List<Map<String,Object>> list(String id) throws IOException {
        var result=new ArrayList<Map<String,Object>>();
        for(Path file:files(id)) { var checkpoint=read(file); checkpoint.remove("resume"); result.add(checkpoint); }
        return result;
    }
    Map<String,Object> get(String id,String version) throws IOException {
        if(!version.matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("Invalid revision");
        return read(directory(id).resolve(version+".json.gz"));
    }
    void delete(String id) throws IOException { for(Path file:files(id)) Files.delete(file); Files.deleteIfExists(directory(id)); }
}
