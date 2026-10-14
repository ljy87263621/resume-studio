package com.resumestudio;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;

/** Small Java API shims for Android 8+. Shared business sources are generated at build time. */
public final class MobileCompat {
    private MobileCompat() {}
    @SafeVarargs public static <T> Set<T> set(T... values) { return Collections.unmodifiableSet(new HashSet<>(Arrays.asList(values))); }
    @SuppressWarnings("unchecked") public static <K,V> Map<K,V> map(Object... values) {
        Map<K,V> result=new LinkedHashMap<>();
        for(int i=0;i<values.length;i+=2) result.put((K)values[i],(V)values[i+1]);
        return Collections.unmodifiableMap(result);
    }
    public static String hex(byte[] bytes) { StringBuilder b=new StringBuilder(); for(byte v:bytes) b.append(String.format(Locale.ROOT,"%02x",v&255)); return b.toString(); }
    public static String readString(Path path) throws IOException { return new String(Files.readAllBytes(path),StandardCharsets.UTF_8); }
    public static Path writeString(Path path,CharSequence value) throws IOException { return Files.write(path,value.toString().getBytes(StandardCharsets.UTF_8)); }
    public static byte[] readNBytes(InputStream in,int limit) throws IOException {
        ByteArrayOutputStream out=new ByteArrayOutputStream(); byte[] block=new byte[8192];
        while(out.size()<limit) { int n=in.read(block,0,Math.min(block.length,limit-out.size())); if(n<0) break; out.write(block,0,n); }
        return out.toByteArray();
    }
    private static final ScheduledExecutorService TIMER=Executors.newSingleThreadScheduledExecutor(r->{Thread t=new Thread(r,"request-deadline"); t.setDaemon(true); return t;});
    public static <T> CompletableFuture<T> timeout(CompletableFuture<T> pending,T fallback) {
        ScheduledFuture<?> task=TIMER.schedule(()->pending.complete(fallback),30,TimeUnit.SECONDS);
        pending.whenComplete((v,e)->task.cancel(false)); return pending;
    }
}
