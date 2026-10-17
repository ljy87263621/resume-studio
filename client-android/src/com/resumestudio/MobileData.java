package com.resumestudio;
import java.nio.file.Path;
import java.io.IOException;
import java.util.*;
/** All foreground, alarm and JobScheduler work shares Store's transaction lock. */
final class MobileData {
    private static final Map<Path,Store> STORES=new HashMap<>();
    private MobileData(){}
    static synchronized Store open(Path path)throws IOException{
        Path root=path.toAbsolutePath().normalize();Store store=STORES.get(root);
        if(store==null){store=new Store(root);STORES.put(root,store);}return store;
    }
}
