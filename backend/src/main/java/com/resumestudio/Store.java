package com.resumestudio;

import java.nio.file.*;
import java.io.IOException;
import java.time.Instant;
import java.util.*;

/** Each document is atomically replaced. The previous complete revision is kept as .bak. */
public final class Store {
    private final Path root;
    public Store(Path root) throws IOException { this.root=root.toAbsolutePath(); Files.createDirectories(this.root); }
    private Path path(String kind,String id) throws IOException {
        if (!Set.of("resumes","favorites","applications","sources").contains(kind)) throw new IllegalArgumentException("Invalid collection");
        if (!id.matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("Invalid identifier");
        Path dir=root.resolve(kind); Files.createDirectories(dir); return dir.resolve(id+".json");
    }
    public synchronized Map<String,Object> save(String kind, Map<String,Object> doc) throws IOException {
        String id=Json.str(doc,"id"); if (id.isBlank()) id=UUID.randomUUID().toString();
        Map<String,Object> copy=new LinkedHashMap<>(doc); copy.put("id",id); copy.put("updatedAt",Instant.now().toString());
        Path p=path(kind,id), temp=p.resolveSibling(p.getFileName()+".tmp");
        if (Files.exists(p)) Files.copy(p,p.resolveSibling(p.getFileName()+".bak"),StandardCopyOption.REPLACE_EXISTING);
        Files.writeString(temp,Json.write(copy));
        try { Files.move(temp,p,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE); }
        catch(AtomicMoveNotSupportedException e) { Files.move(temp,p,StandardCopyOption.REPLACE_EXISTING); }
        return copy;
    }
    public synchronized List<Map<String,Object>> list(String kind) throws IOException {
        Path dir=path(kind,"probe").getParent(); List<Map<String,Object>> docs=new ArrayList<>();
        try (var files=Files.list(dir)) {
            for(Path p:files.filter(x->x.toString().endsWith(".json")).toList()) docs.add(Json.object(Json.parse(Files.readString(p))));
        }
        docs.sort(Comparator.comparing((Map<String,Object> m)->Json.str(m,"updatedAt")).reversed()); return docs;
    }
    public synchronized void delete(String kind,String id) throws IOException {
        Path p=path(kind,id); Files.deleteIfExists(p); Files.deleteIfExists(p.resolveSibling(p.getFileName()+".bak"));
    }
}
