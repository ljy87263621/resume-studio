package com.resumestudio;

import android.app.*;
import android.content.*;
import android.os.*;
import android.view.*;
import android.webkit.*;
import org.json.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Consumer;

/** Exercises the signed production PDF extractor, bundled OCR and confirmation UI without a shipped test bridge. */
public final class ResumePdfInstrumentation extends Instrumentation {
    private WebView web;
    @Override public void onCreate(Bundle args){super.onCreate(args);start();}
    private WebView find(View view){if(view instanceof WebView)return (WebView)view;if(view instanceof ViewGroup group)for(int i=0;i<group.getChildCount();i++){WebView found=find(group.getChildAt(i));if(found!=null)return found;}return null;}
    private String js(String script)throws Exception{CompletableFuture<String> result=new CompletableFuture<>();runOnMainSync(()->web.evaluateJavascript(script,result::complete));return result.get(120,TimeUnit.SECONDS);}
    @Override public void onStart(){Bundle report=new Bundle();try{
        Intent intent=new Intent().setClassName("com.resumestudio.mobile","com.resumestudio.MainActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);Activity activity=startActivitySync(intent);runOnMainSync(()->web=find(activity.getWindow().getDecorView()));
        for(int i=0;i<150;i++){if("true".equals(js("typeof actions==='object'&&state.templates.length===14")))break;Thread.sleep(200);}
        Thread.sleep(1200);js("window.pdfTestReady=false;(async()=>{await navigate('resumes');window.pdfTestReady=true;})()");
        for(int i=0;i<100;i++){if("true".equals(js("window.pdfTestReady===true&&!pageTransitionActive")))break;Thread.sleep(100);}
        Class<?> type=Class.forName("com.resumestudio.MobileResumePdf",true,getTargetContext().getClassLoader());var read=type.getDeclaredMethod("read",Context.class,String.class,byte[].class,Consumer.class);read.setAccessible(true);
        int checks=0;
        for(String name:new String[]{"text","scan","mixed","columns"}){
            byte[] bytes;try(InputStream in=getContext().getAssets().open(name+".pdf")){bytes=in.readAllBytes();}
            @SuppressWarnings("unchecked") Map<String,Object> draft=(Map<String,Object>)read.invoke(null,getTargetContext(),name+".pdf",bytes,(Consumer<String>)(s)->{});
            Map<?,?> profile=(Map<?,?>)draft.get("profile");if(!profile.get("phone").toString().contains("13800138000")||!profile.get("email").toString().contains("example.com"))throw new Exception(name+": OCR contact fields missing: "+draft);
            if(((Number)draft.get("ocrPages")).intValue()!=(name.equals("text")||name.equals("columns")?0:1))throw new Exception(name+": wrong OCR page count");checks+=2;
            String encoded=new JSONObject(draft).toString();js("window.resumeImportChecks=(async()=>{await flush();window.importBefore=(await api('list',{kind:'resumes'})).length;await window.nativeEvent('resumeDocument',"+encoded+");return true;})()");
            for(int i=0;i<100;i++){if("true".equals(js("document.querySelector('#resume-import-template')!==null")))break;Thread.sleep(100);}
            if(!"true".equals(js("document.querySelector('#resume-import-template').options.length===14&&state.resumeImportDraft.sections.length>=2")))throw new Exception(name+": import review missing");checks++;
            js("document.querySelector('[data-import-profile=\"name\"]').value='PDF确认测试';window.resumeImportSaved=(async()=>{await actions.confirmResumeImport();return state.current.id;})().then(id=>window.savedPdfImport=id).catch(e=>window.savedPdfImport='ERROR '+e.message)");
            for(int i=0;i<100;i++){if("true".equals(js("typeof window.savedPdfImport==='string'")))break;Thread.sleep(100);}
            if(!"true".equals(js("state.current.profile.name==='PDF确认测试'&&state.current.sections.some(s=>s.content.includes('UART'))")))throw new Exception(name+": confirmed template resume did not preserve edited profile and project: "+js("JSON.stringify({saved:window.savedPdfImport,current:state.current,draft:state.resumeImportDraft})"));checks++;
            if(!"true".equals(js("window.resumeImportChecks=(async()=>{const rows=await api('list',{kind:'resumes'});return rows.length===window.importBefore+1;})().then(ok=>window.importCountOk=ok);true")))throw new Exception("test script failed");
            Thread.sleep(300);if(!"true".equals(js("window.importCountOk")))throw new Exception("Existing resumes were replaced");checks++;
            js("delete window.savedPdfImport;delete window.importCountOk;");
        }
        report.putString("result","PASS: "+checks+" production Android PDF text/OCR/review/create checks");finish(0,report);
    }catch(Throwable error){report.putString("error",android.util.Log.getStackTraceString(error));finish(1,report);}}
}
