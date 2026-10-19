package com.resumestudio;

import java.io.*;
import java.net.*;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;

/** Deterministic JVM coverage for interrupted and resumed Android APK transfers. */
public final class MobileHttpRetryTests {
    private static final class FakeConnection extends HttpURLConnection {
        private final int status;
        private final byte[] bytes;
        private final String contentRange;
        private final boolean interruptAfterPrefix;
        FakeConnection(URI uri,int status,byte[] bytes,String contentRange,boolean interruptAfterPrefix)throws IOException{
            super(uri.toURL());this.status=status;this.bytes=bytes;this.contentRange=contentRange;this.interruptAfterPrefix=interruptAfterPrefix;
        }
        @Override public void connect(){}
        @Override public void disconnect(){}
        @Override public boolean usingProxy(){return false;}
        @Override public int getResponseCode(){return status;}
        @Override public long getContentLengthLong(){return bytes.length;}
        @Override public String getHeaderField(String name){return name.equalsIgnoreCase("Content-Range")?contentRange:null;}
        @Override public InputStream getInputStream(){
            if(!interruptAfterPrefix)return new ByteArrayInputStream(bytes);
            return new InputStream(){int offset;boolean interrupted;
                @Override public int read(byte[] target,int start,int length)throws IOException{
                    if(interruptAfterPrefix&&offset>=4096&&!interrupted){interrupted=true;throw new SocketTimeoutException("simulated read timeout");}
                    if(offset>=bytes.length)return -1;
                    int count=Math.min(length,Math.min(4096,bytes.length-offset));System.arraycopy(bytes,offset,target,start,count);offset+=count;return count;
                }
                @Override public int read()throws IOException{byte[] one=new byte[1];int count=read(one,0,1);return count<0?-1:one[0]&255;}
            };
        }
    }
    private static final class NoCancel implements MobileHttp.Transfer {
        public void progress(long bytes,long total){}
        public boolean cancelled(){return false;}
    }
    public static void main(String[] args)throws Exception{
        URI uri=URI.create("https://github.com/ljy87263621/resume-studio/releases/download/test/app.apk");
        byte[] payload=new byte[128*1024];new Random(42).nextBytes(payload);
        Path dir=Files.createTempDirectory("android-update-retry-");
        try{
            Path resumed=dir.resolve("resumed.apk");int[] attempts={0};
            String resumedHash=MobileHttp.download(uri,resumed,payload.length,payload.length,new NoCancel(),(target,offset)->{
                attempts[0]++;
                if(offset==0)return new FakeConnection(target,200,payload,null,true);
                return new FakeConnection(target,206,Arrays.copyOfRange(payload,(int)offset,payload.length),"bytes "+offset+"-"+(payload.length-1)+"/"+payload.length,false);
            });
            if(attempts[0]!=2||!Arrays.equals(payload,Files.readAllBytes(resumed))||!resumedHash.equals(sha256(payload)))throw new AssertionError("Interrupted APK did not resume and verify");

            Path restarted=dir.resolve("restarted.apk");int[] restartAttempts={0};
            MobileHttp.download(uri,restarted,payload.length,payload.length,new NoCancel(),(target,offset)->{
                restartAttempts[0]++;
                if(offset==0)return new FakeConnection(target,200,payload,null,true);
                return new FakeConnection(target,200,payload,null,false); // Server ignores Range: restart cleanly.
            });
            if(restartAttempts[0]!=2||!Arrays.equals(payload,Files.readAllBytes(restarted)))throw new AssertionError("Ignoring Range duplicated or corrupted APK bytes");
            System.out.println("PASS: interrupted APK downloads resume from the saved byte offset and restart cleanly when Range is ignored");
        }finally{try(var files=Files.list(dir)){files.forEach(file->{try{Files.deleteIfExists(file);}catch(IOException ignored){}});}Files.deleteIfExists(dir);}
    }
    private static String sha256(byte[] bytes)throws Exception{
        StringBuilder result=new StringBuilder();for(byte value:MessageDigest.getInstance("SHA-256").digest(bytes))result.append(String.format(Locale.ROOT,"%02x",value&255));return result.toString();
    }
}
