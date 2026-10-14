package com.resumestudio;

import android.content.Context;
import java.io.*;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.security.spec.*;
import java.util.*;

/** Verifies the same RSA-PSS signed GitHub channel data as the desktop updater. */
final class AndroidUpdateClient {
    static final String REPOSITORY="scj040921/resume-studio";
    static final long MAX_APK=150L*1024L*1024L;
    static final class Release {
        final String channel,version,versionName,tag,commit,sha256,packageName;
        final int versionCode; final long size;
        Release(Map<String,Object> value){channel=Json.str(value,"channel");version=Json.str(value,"version");versionName=Json.str(value,"androidVersionName");tag=Json.str(value,"tag");commit=Json.str(value,"commit");sha256=Json.str(value,"androidSha256");packageName=Json.str(value,"androidPackageName");versionCode=((Number)value.get("androidVersionCode")).intValue();size=((Number)value.get("androidPackageSize")).longValue();}
        Map<String,Object> display(){return MobileCompat.map("versionName",versionName,"tag",tag,"packageName",packageName,"size",size);}
    }
    private final Context context; private final String publicKey;
    AndroidUpdateClient(Context context)throws IOException{this.context=context.getApplicationContext();try(InputStream input=context.getAssets().open("UpdatePublicKey.pem")){publicKey=new String(MobileCompat.readNBytes(input,8192),StandardCharsets.US_ASCII);}}

    Release check(String channel,int installedVersionCode,String route)throws Exception{
        if(!channel.equals("stable")&&!channel.equals("nightly"))throw new IOException("更新通道无效");
        URI index=URI.create("https://github.com/"+REPOSITORY+"/releases/download/update-channels/"+channel+".json");
        byte[] envelope=read(index,route,64000);Map<String,Object> signed=Json.object(Json.parse(new String(envelope,StandardCharsets.UTF_8)));
        byte[] data=Base64.getDecoder().decode(Json.str(signed,"data"));String signature=Json.str(signed,"signature");
        if(data.length>16000)throw new IOException("签名索引过大");
        Signature verifier=Signature.getInstance("RSASSA-PSS");verifier.initVerify(KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(pem(publicKey))));verifier.setParameter(new PSSParameterSpec("SHA-256","MGF1",MGF1ParameterSpec.SHA256,32,1));verifier.update(data);
        if(!verifier.verify(Base64.getDecoder().decode(signature)))throw new IOException("更新签名校验失败，已停止检查");
        Map<String,Object> metadata=Json.object(Json.parse(new String(data,StandardCharsets.UTF_8)));
        if(!channel.equals(Json.str(metadata,"channel")))throw new IOException("签名更新通道不匹配");
        String version=Json.str(metadata,"version"),tag=Json.str(metadata,"tag"),commit=Json.str(metadata,"commit"),name=Json.str(metadata,"androidPackageName"),sha=Json.str(metadata,"androidSha256"),versionName=Json.str(metadata,"androidVersionName");
        int code=((Number)metadata.get("androidVersionCode")).intValue();long size=((Number)metadata.get("androidPackageSize")).longValue();
        if(!version.matches("\\d+\\.\\d+\\.\\d+")||!commit.matches("[0-9a-f]{40}")||!sha.matches("[0-9a-f]{64}")||code<1||size<1||size>MAX_APK)throw new IOException("签名 Android 发行信息无效");
        if(channel.equals("stable")?!tag.equals("v"+version):!tag.matches("nightly-\\d{8}-[0-9a-f]{8}")||!tag.endsWith(commit.substring(0,8)))throw new IOException("更新标签与签名提交不匹配");
        String expected=channel.equals("stable")?"ResumeStudio-Android-v"+version+".apk":"ResumeStudio-Android-v"+version+"-nightly."+tag.substring(8).replace('-','.')+".apk";
        String expectedVersionName=channel.equals("stable")?version:version+"-nightly."+tag.substring(8).replace('-','.');
        if(!name.equals(expected)||!versionName.equals(expectedVersionName))throw new IOException("APK 名称或版本格式无效");
        if(code<=installedVersionCode)return null;
        return new Release(metadata);
    }

    Path download(Release release,String route)throws IOException{
        Path path=context.getCacheDir().toPath().resolve("android-update.apk");Files.createDirectories(path.getParent());
        URI uri=URI.create("https://github.com/"+REPOSITORY+"/releases/download/"+release.tag+"/"+release.packageName);
        String hash=downloadWithRoute(uri,route,path,release.size);
        if(!release.sha256.equals(hash)){Files.deleteIfExists(path);throw new IOException("APK 摘要校验失败，未安装更新");}return path;
    }

    private byte[] read(URI uri,String route,int limit)throws IOException{
        IOException failure=null;for(URI target:routes(uri,route))try{MobileHttp.Result response=MobileHttp.request(target,"GET",Map.of("Cache-Control","no-cache","User-Agent","ResumeStudio-Android"),null,limit);if(response.status!=200)throw new IOException("GitHub 更新通道返回 HTTP "+response.status);return response.bytes;}catch(IOException ex){failure=ex;}
        throw new IOException("无法连接 GitHub 更新通道",failure);
    }
    private String downloadWithRoute(URI uri,String route,Path path,long size)throws IOException{
        IOException failure=null;for(URI target:routes(uri,route))try{return MobileHttp.download(target,path,size,MAX_APK);}catch(IOException ex){failure=ex;}
        throw new IOException("APK 下载或摘要校验未完成",failure);
    }
    private static List<URI> routes(URI uri,String route){if(!route.equals("auto")&&!route.equals("direct")&&!route.equals("proxy"))throw new IllegalArgumentException("更新线路无效");List<URI> urls=new ArrayList<>();if(!route.equals("proxy"))urls.add(uri);if(!route.equals("direct"))urls.add(URI.create("https://gh-proxy.org/"+uri));return urls;}
    private static byte[] pem(String text)throws GeneralSecurityException{String value=text.replace("-----BEGIN PUBLIC KEY-----","").replace("-----END PUBLIC KEY-----","").replaceAll("\\s","");return Base64.getDecoder().decode(value);}
}
