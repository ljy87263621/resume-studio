package com.resumestudio;

import android.app.*;
import android.content.*;
import android.os.*;
import android.view.*;
import android.webkit.*;
import android.graphics.Bitmap;
import android.graphics.pdf.PdfRenderer;
import android.print.*;
import org.json.*;
import java.io.*;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.concurrent.*;

/** Runs against the signed production APK; no test bridge or debugging mode is shipped in the app. */
public final class AcceptanceInstrumentation extends Instrumentation {
    private WebView web;private String davPort;private boolean onlineUpdate,installerOnly;
    @Override public void onCreate(Bundle args){super.onCreate(args);davPort=args.getString("davPort","");onlineUpdate=Boolean.parseBoolean(args.getString("onlineUpdate","false"));installerOnly=Boolean.parseBoolean(args.getString("installerOnly","false"));start();}
    private WebView find(View v){if(v instanceof WebView)return(WebView)v;if(v instanceof ViewGroup g)for(int i=0;i<g.getChildCount();i++){WebView w=find(g.getChildAt(i));if(w!=null)return w;}return null;}
    private String js(String script)throws Exception{CompletableFuture<String> result=new CompletableFuture<>();runOnMainSync(()->web.evaluateJavascript(script,result::complete));return result.get(120,TimeUnit.SECONDS);}
    @Override public void onStart(){Bundle results=new Bundle();try{
        verifyUpdaterSignature();verifyAndroidPlatformChannel();verifyInstallerStatusPendingIntent();verifyInstallerWriteClosure();
        if(installerOnly){results.putString("report","PASS: production installer closes APK write stream before commit");finish(0,results);return;}
        Intent i=new Intent();i.setClassName("com.resumestudio.mobile","com.resumestudio.MainActivity");i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);Activity a=startActivitySync(i);runOnMainSync(()->web=find(a.getWindow().getDecorView()));
        for(int n=0;n<100;n++){if("true".equals(js("typeof actions==='object' && state.templates.length===14 && document.querySelector('#content h1')!==null")))break;Thread.sleep(200);}
        verifyBrowserSafeArea(a);
        js("window.testDavPort="+JSONObject.quote(davPort));String script;try(InputStream in=getContext().getAssets().open("acceptance.js")){script=new String(in.readAllBytes(),java.nio.charset.StandardCharsets.UTF_8);}js(script);
        String report="null";for(int n=0;n<600;n++){report=js("window.acceptanceResult||null");if(!report.equals("null"))break;Thread.sleep(200);}
        File dir=new File(getTargetContext().getExternalFilesDir(null),"acceptance");dir.mkdirs();
        try(FileOutputStream out=new FileOutputStream(new File(dir,"result.json"))){out.write(report.getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        js("document.querySelector('#notice').hidden=true;document.querySelector('#content').classList.remove('page-enter');true");Thread.sleep(450);Bitmap bitmap=getUiAutomation().takeScreenshot();try(FileOutputStream out=new FileOutputStream(new File(dir,"screen.png"))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
        if(!report.contains("\"error\"")&&!report.equals("null")){
            String html=(String)new JSONTokener(js("window.exportFixtureHtml")).nextValue();File pdf=export(a,dir,html);
            try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(pdf,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd)){
                if(renderer.getPageCount()<1||pdf.length()<1000)throw new IOException("Android PDF export empty");
                try(PdfRenderer.Page page=renderer.openPage(0)){Bitmap image=Bitmap.createBitmap(page.getWidth()*2,page.getHeight()*2,Bitmap.Config.ARGB_8888);image.eraseColor(android.graphics.Color.WHITE);page.render(image,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);try(FileOutputStream out=new FileOutputStream(new File(dir,"resume-pdf.png"))){image.compress(Bitmap.CompressFormat.PNG,100,out);}image.recycle();}
                verifyImports(pdf);JSONObject json=new JSONObject(report);json.put("pdfPages",renderer.getPageCount());json.put("pdfBytes",pdf.length());json.put("nativeImportChecks",4);json.put("liveSignedUpdate",onlineUpdate);report=json.toString();
                try(FileOutputStream out=new FileOutputStream(new File(dir,"result.json"))){out.write(report.getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            }
        }
        js("navigate('settings');true");Thread.sleep(900);
        js("setTheme({theme:'dark',interfaceStyle:'haru',motionEnabled:false});document.querySelector('#update-result').closest('.card').scrollIntoView({block:'center'});true");Thread.sleep(500);
        capture(dir,"dark-settings.png");
        js("navigate('home');true");Thread.sleep(500);capture(dir,"bottom-navigation.png");
        js("setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:false});true");Thread.sleep(400);capture(dir,"light-settings.png");
        results.putString("report",report);finish(report.contains("\\\"error\\\"")||report.contains("\"error\"")||report.equals("null")?1:0,results);
    }catch(Exception ex){Throwable cause=ex;while(cause.getCause()!=null)cause=cause.getCause();android.util.Log.e("ResumeStudioTest","Acceptance failed",ex);results.putString("error",cause.toString());finish(1,results);}}
    private void capture(File dir,String name)throws IOException {
        Bitmap image=getUiAutomation().takeScreenshot();try(FileOutputStream out=new FileOutputStream(new File(dir,name))){image.compress(Bitmap.CompressFormat.PNG,100,out);}image.recycle();
    }
    private void verifyBrowserSafeArea(Activity activity)throws Exception{
        Dialog[] dialog=new Dialog[1];WebView[] page=new WebView[1];android.widget.Button[] captureButton=new android.widget.Button[1];
        Method browser=activity.getClass().getDeclaredMethod("browser",String.class);browser.setAccessible(true);
        CompletableFuture<Void> loaded=new CompletableFuture<>();
        runOnMainSync(()->{try{
            dialog[0]=(Dialog)browser.invoke(activity,"https://www.zhipin.com/job_detail/inset-regression.html");
            ViewGroup root=(ViewGroup)dialog[0].findViewById(android.R.id.content);page[0]=find(root);page[0].stopLoading();
            ViewGroup layout=(ViewGroup)page[0].getParent();ViewGroup bar=(ViewGroup)layout.getChildAt(0);captureButton[0]=(android.widget.Button)bar.getChildAt(1);
            page[0].setWebViewClient(new WebViewClient(){@Override public void onPageFinished(WebView v,String url){loaded.complete(null);}});
            page[0].loadDataWithBaseURL("https://www.zhipin.com/","<html><body>职位收藏按钮安全区域验收</body></html>","text/html","UTF-8",null);
        }catch(Exception ex){throw new RuntimeException(ex);}});
        try{
            loaded.get(30,TimeUnit.SECONDS);Thread.sleep(400);
            runOnMainSync(()->{
                ViewGroup layout=(ViewGroup)page[0].getParent();int[] position=new int[2];captureButton[0].getLocationOnScreen(position);
                WindowInsets insets=layout.getRootWindowInsets();int top=Build.VERSION.SDK_INT>=30?insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()).top:insets.getSystemWindowInsetTop();
                if(position[1]<top||captureButton[0].getHeight()<48*activity.getResources().getDisplayMetrics().density)throw new AssertionError("Browser capture button overlaps status bar or is too small");
                if(Build.VERSION.SDK_INT>=30){
                    WindowInsets cutout=new WindowInsets.Builder().setInsets(WindowInsets.Type.systemBars(),android.graphics.Insets.of(8,96,12,72)).setInsets(WindowInsets.Type.displayCutout(),android.graphics.Insets.of(40,120,20,0)).build();
                    layout.dispatchApplyWindowInsets(cutout);
                    if(layout.getPaddingLeft()!=40||layout.getPaddingTop()!=120||layout.getPaddingRight()!=20||layout.getPaddingBottom()!=72)throw new AssertionError("Browser does not respect system bars and display cutouts on every edge");
                    layout.requestApplyInsets();
                }
            });
            Thread.sleep(300);File dir=new File(getTargetContext().getExternalFilesDir(null),"acceptance");dir.mkdirs();capture(dir,"browser-safe-area.png");
            runOnMainSync(()->{if(!captureButton[0].performClick())throw new AssertionError("Browser capture button cannot be clicked");});
            for(int attempt=0;attempt<50;attempt++){
                boolean[] complete=new boolean[1];runOnMainSync(()->{ViewGroup layout=(ViewGroup)page[0].getParent();complete[0]=captureButton[0].isEnabled()&&((android.widget.TextView)layout.getChildAt(1)).getText().toString().contains("未读取到完整职位正文");});
                if(complete[0])return;Thread.sleep(100);
            }
            throw new IOException("Browser capture click did not reach the collection handler");
        }finally{runOnMainSync(()->dialog[0].dismiss());}
    }
    private void verifyUpdaterSignature()throws Exception{
        JSONObject signed;try(InputStream in=getContext().getAssets().open("stable-channel.json")){signed=new JSONObject(new String(in.readAllBytes(),StandardCharsets.UTF_8));}
        byte[] data=Base64.getDecoder().decode(signed.getString("data")),signature=Base64.getDecoder().decode(signed.getString("signature"));
        String publicKey;try(InputStream in=getTargetContext().getAssets().open("UpdatePublicKey.pem")){publicKey=new String(in.readAllBytes(),StandardCharsets.US_ASCII);}
        Class<?> verifier=Class.forName("com.resumestudio.AndroidUpdateClient",true,getTargetContext().getClassLoader());Method method=verifier.getDeclaredMethod("verifySignature",String.class,byte[].class,byte[].class);method.setAccessible(true);
        if(!Boolean.TRUE.equals(method.invoke(null,publicKey,data,signature)))throw new IOException("Production Android RSA-PSS verifier rejected signed release fixture");
        data[0]^=1;if(Boolean.TRUE.equals(method.invoke(null,publicKey,data,signature)))throw new IOException("Production Android RSA-PSS verifier accepted modified metadata");
        Class<?> transport=Class.forName("com.resumestudio.MobileHttp",true,getTargetContext().getClassLoader());
        Method readUpdate=transport.getDeclaredMethod("readUpdate",java.net.URI.class,int.class);
        for(String rejected:new String[]{"http://github.com/file","https://evil.example/file","https://user@github.com/file","https://github.com:444/file"}){
            try{readUpdate.invoke(null,java.net.URI.create(rejected),64000);throw new IOException("Unsafe update destination accepted: "+rejected);}
            catch(java.lang.reflect.InvocationTargetException ex){if(!(ex.getCause() instanceof IOException))throw ex;}
        }
        if(onlineUpdate){
            var constructor=verifier.getDeclaredConstructor(android.content.Context.class);constructor.setAccessible(true);Object client=constructor.newInstance(getTargetContext());
            Method check=verifier.getDeclaredMethod("check",String.class,int.class,String.class);check.setAccessible(true);
            Object release=check.invoke(client,"nightly",0,"auto");if(release==null)throw new IOException("Live signed nightly channel unavailable");
            Method download=verifier.getDeclaredMethod("download",release.getClass(),String.class);download.setAccessible(true);
            java.nio.file.Path downloaded=(java.nio.file.Path)download.invoke(client,release,"auto");
            if(java.nio.file.Files.size(downloaded)<1000000)throw new IOException("Live APK download failed");
            var pm=getTargetContext().getPackageManager();
            var archive=pm.getPackageArchiveInfo(downloaded.toString(),android.content.pm.PackageManager.GET_SIGNATURES);
            var installed=pm.getPackageInfo(getTargetContext().getPackageName(),android.content.pm.PackageManager.GET_SIGNATURES);
            if(archive==null||!java.util.Arrays.equals(archive.signatures[0].toByteArray(),installed.signatures[0].toByteArray()))throw new IOException("Hotfix signing certificate differs from the published APK");
            android.util.Log.i("ResumeStudioTest","Live signed GitHub channel, redirect, APK size and SHA-256 passed");
        }

    }
    private void verifyInstallerStatusPendingIntent()throws Exception{
        Class<?> receiver=Class.forName("com.resumestudio.MobileInstallReceiver",true,getTargetContext().getClassLoader());
        Method factory=receiver.getMethod("statusPendingIntent",Context.class,int.class);
        PendingIntent status=(PendingIntent)factory.invoke(null,getTargetContext(),0x5253);
        try{if(Build.VERSION.SDK_INT>=23&&status.isImmutable())throw new IOException("PackageInstaller status PendingIntent must be mutable so Android can return the commit result");}
        finally{status.cancel();}
    }
    private void verifyAndroidPlatformChannel()throws Exception{
        java.security.KeyPairGenerator generator=java.security.KeyPairGenerator.getInstance("RSA");generator.initialize(2048);var keys=generator.generateKeyPair();
        String publicKey="-----BEGIN PUBLIC KEY-----\n"+Base64.getEncoder().encodeToString(keys.getPublic().getEncoded())+"\n-----END PUBLIC KEY-----";
        JSONObject metadata=new JSONObject().put("platform","android").put("channel","nightly").put("version","3.2.1").put("commit","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").put("tag","android-nightly-20261016-aaaaaaaa").put("androidVersionName","3.2.1-nightly.20261016.aaaaaaaa").put("androidPackageName","ResumeStudio-Android-v3.2.1-nightly.20261016.aaaaaaaa.apk").put("androidVersionCode",321000001).put("androidPackageSize",1234).put("androidSha256","0000000000000000000000000000000000000000000000000000000000000000");
        Class<?> client=Class.forName("com.resumestudio.AndroidUpdateClient",true,getTargetContext().getClassLoader());
        Method verify=client.getDeclaredMethod("verifyRelease",byte[].class,String.class,String.class,int.class);verify.setAccessible(true);
        for(String scenario:new String[]{"valid","same-version","desktop","wrong-channel","legacy-tag","tampered"}){
            JSONObject value=new JSONObject(metadata.toString());if(scenario.equals("desktop"))value.put("platform","desktop");if(scenario.equals("legacy-tag"))value.put("tag","nightly-20261016-aaaaaaaa");
            byte[] data=value.toString().getBytes(StandardCharsets.UTF_8);java.security.Signature signer=java.security.Signature.getInstance("SHA256withRSA/PSS");signer.setParameter(new java.security.spec.PSSParameterSpec("SHA-256","MGF1",java.security.spec.MGF1ParameterSpec.SHA256,32,1));signer.initSign(keys.getPrivate());signer.update(data);byte[] signature=signer.sign();if(scenario.equals("tampered"))data[5]^=1;
            byte[] envelope=new JSONObject().put("data",Base64.getEncoder().encodeToString(data)).put("signature",Base64.getEncoder().encodeToString(signature)).toString().getBytes(StandardCharsets.UTF_8);
            boolean rejected=false;Object result=null;
            try{result=verify.invoke(null,envelope,publicKey,scenario.equals("wrong-channel")?"stable":"nightly",scenario.equals("same-version")?321000001:0);}catch(java.lang.reflect.InvocationTargetException ex){if(!(ex.getCause() instanceof IOException))throw ex;rejected=true;}
            if(scenario.equals("valid")){if(rejected||result==null)throw new IOException("Independent Android channel rejected");}
            else if(scenario.equals("same-version")){if(rejected||result!=null)throw new IOException("Same Android version advertised again");}
            else if(!rejected)throw new IOException("Android channel accepted "+scenario);
        }
    }
    private void verifyInstallerWriteClosure()throws Exception{
        Context context=getTargetContext();android.content.pm.PackageInstaller installer=context.getPackageManager().getPackageInstaller();
        var params=new android.content.pm.PackageInstaller.SessionParams(android.content.pm.PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        int id=installer.createSession(params);File apk=File.createTempFile("installer-regression-",".apk",context.getCacheDir());
        // Deliberately invalid APK: commit must return normally, then Android rejects it asynchronously.
        // An open write descriptor instead throws "Files still open" synchronously on the affected Android versions.
        java.nio.file.Files.write(apk.toPath(),new byte[]{0x50,0x4b,0x03,0x04});
        Intent callback=new Intent(context,Class.forName("com.resumestudio.MobileInstallReceiver",true,context.getClassLoader()));
        PendingIntent pending=PendingIntent.getBroadcast(context,id,callback,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_MUTABLE);
        try(android.content.pm.PackageInstaller.Session session=installer.openSession(id)){
            Class<?> activity=Class.forName("com.resumestudio.MainActivity",true,context.getClassLoader());
            Method write=activity.getDeclaredMethod("writeAndCommitInstallSession",android.content.pm.PackageInstaller.Session.class,java.nio.file.Path.class,IntentSender.class);write.setAccessible(true);
            write.invoke(null,session,apk.toPath(),pending.getIntentSender());
        }finally{apk.delete();try{installer.abandonSession(id);}catch(SecurityException ignored){}pending.cancel();}
    }
    private File export(Activity activity,File dir,String html)throws Exception{
        String name="android-resume-acceptance-"+System.currentTimeMillis();js("api('print',{title:"+JSONObject.quote(name)+",html:window.exportFixtureHtml});true");
        Thread.sleep(2500);waitClick("com.android.printspooler:id/destination_spinner",null);Thread.sleep(700);waitClick(null,"Save as PDF");Thread.sleep(1000);
        waitClick("com.android.printspooler:id/print_button",null);Thread.sleep(1500);
        StringBuilder tree=new StringBuilder();describe(getUiAutomation().getRootInActiveWindow(),tree,0);
        try(FileOutputStream out=new FileOutputStream(new File(dir,"print-tree.txt"))){out.write(tree.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        Bitmap screenshot=getUiAutomation().takeScreenshot();try(FileOutputStream out=new FileOutputStream(new File(dir,"print-screen.png"))){screenshot.compress(Bitmap.CompressFormat.PNG,100,out);}screenshot.recycle();
        waitClick("android:id/button1",null);Thread.sleep(2500);
        byte[] bytes=new byte[0];for(int attempt=0;attempt<20&&bytes.length<1000;attempt++){try(ParcelFileDescriptor output=getUiAutomation().executeShellCommand("cat /sdcard/Download/"+name+".pdf");InputStream input=new ParcelFileDescriptor.AutoCloseInputStream(output)){bytes=input.readAllBytes();}if(bytes.length<1000)Thread.sleep(500);}
        if(bytes.length<1000||bytes[0]!='%'||bytes[1]!='P')throw new IOException("System PDF file was not saved");File pdf=new File(dir,"resume.pdf");try(FileOutputStream out=new FileOutputStream(pdf)){out.write(bytes);}return pdf;
    }
    private void describe(android.view.accessibility.AccessibilityNodeInfo node,StringBuilder out,int depth){if(node==null)return;out.append(" ".repeat(depth)).append(node.getViewIdResourceName()).append(" | ").append(node.getText()).append(" | ").append(node.getContentDescription()).append('\n');for(int n=0;n<node.getChildCount();n++)describe(node.getChild(n),out,depth+1);}
    private boolean click(String id,String text){var root=getUiAutomation().getRootInActiveWindow();return clickNode(root,id,text);}
    private void waitClick(String id,String text)throws Exception{for(int n=0;n<150;n++){if(click(id,text))return;Thread.sleep(200);}throw new IOException("Control unavailable: "+id+" "+text);}
    private boolean clickNode(android.view.accessibility.AccessibilityNodeInfo node,String id,String text){if(node==null)return false;if((id!=null&&id.equals(node.getViewIdResourceName()))||(text!=null&&text.contentEquals(node.getText()==null?"":node.getText()))){android.graphics.Rect bounds=new android.graphics.Rect();node.getBoundsInScreen(bounds);if(bounds.isEmpty())return false;long time=SystemClock.uptimeMillis();android.view.MotionEvent down=android.view.MotionEvent.obtain(time,time,android.view.MotionEvent.ACTION_DOWN,bounds.centerX(),bounds.centerY(),0),up=android.view.MotionEvent.obtain(time,time+70,android.view.MotionEvent.ACTION_UP,bounds.centerX(),bounds.centerY(),0);down.setSource(android.view.InputDevice.SOURCE_TOUCHSCREEN);up.setSource(android.view.InputDevice.SOURCE_TOUCHSCREEN);getUiAutomation().injectInputEvent(down,true);getUiAutomation().injectInputEvent(up,true);down.recycle();up.recycle();return true;}for(int n=0;n<node.getChildCount();n++)if(clickNode(node.getChild(n),id,text))return true;return false;}
    private void verifyImports(File pdf)throws Exception{
        Class<?> importer=Class.forName("com.resumestudio.MobileImport",true,getTargetContext().getClassLoader());var read=importer.getDeclaredMethod("read",String.class,byte[].class);read.setAccessible(true);
        for(String extension:new String[]{"md","txt"}){Object result=read.invoke(null,"fixture."+extension,"## 什么是 TCP？\n可靠字节流协议。".getBytes(java.nio.charset.StandardCharsets.UTF_8));if(!result.toString().contains("TCP"))throw new IOException("Text import failed");}
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();try(java.util.zip.ZipOutputStream zip=new java.util.zip.ZipOutputStream(bytes)){zip.putNextEntry(new java.util.zip.ZipEntry("word/document.xml"));zip.write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:pPr><w:outlineLvl w:val=\"1\"/></w:pPr><w:r><w:t>什么是 TCP？</w:t></w:r></w:p><w:p><w:r><w:t>可靠字节流协议。</w:t></w:r></w:p></w:body></w:document>".getBytes(java.nio.charset.StandardCharsets.UTF_8));zip.closeEntry();}
        if(!read.invoke(null,"fixture.docx",bytes.toByteArray()).toString().contains("TCP"))throw new IOException("DOCX import failed");
        byte[] pdfBytes;try(InputStream in=new FileInputStream(pdf)){pdfBytes=in.readAllBytes();}if(!read.invoke(null,"fixture.pdf",pdfBytes).toString().contains("LVGL"))throw new IOException("PDF text import failed");
    }
}
