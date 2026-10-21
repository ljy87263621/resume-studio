package com.resumestudio;

import android.content.Context;
import android.graphics.*;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.chinese.ChineseTextRecognizerOptions;
import com.tom_roush.pdfbox.pdmodel.PDDocument;
import com.tom_roush.pdfbox.text.PDFTextStripper;
import java.io.*;
import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/** PDF text and bundled on-device Chinese/Latin OCR. The original document stays in the picker. */
final class MobileResumePdf {
    static Map<String,Object> read(Context context,String name,byte[] bytes,Consumer<String> progress)throws Exception {
        if(bytes.length==0||bytes.length>20*1024*1024)throw new IOException("PDF 应为 1 字节至 20 MiB");
        if(!name.toLowerCase(Locale.ROOT).endsWith(".pdf"))throw new IOException("请选择 PDF 简历");
        List<String> pages=new ArrayList<>();Set<Integer> scannedPages=new HashSet<>();
        try(PDDocument pdf=PDDocument.load(bytes)){
            if(pdf.getNumberOfPages()>20)throw new IOException("简历 PDF 最多支持 20 页，请拆分后导入");
            List<com.tom_roush.pdfbox.text.TextPosition> positions=new ArrayList<>();
            PDFTextStripper stripper=new PDFTextStripper(){@Override protected void writeString(String text,List<com.tom_roush.pdfbox.text.TextPosition> letters){positions.addAll(letters);}};stripper.setSortByPosition(true);
            for(int page=1;page<=pdf.getNumberOfPages();page++){positions.clear();stripper.setStartPage(page);stripper.setEndPage(page);stripper.getText(pdf);var pdfPage=pdf.getPage(page-1);var size=pdfPage.getCropBox();String text=MobilePdfLayout.text(positions,size.getWidth(),size.getHeight());if(characters(text)<30)scannedPages.add(page-1);else if(characters(text)<350&&pdfPage.getResources()!=null){for(var key:pdfPage.getResources().getXObjectNames()){var image=pdfPage.getResources().getXObject(key);if(image instanceof com.tom_roush.pdfbox.pdmodel.graphics.image.PDImageXObject scan&&scan.getWidth()>size.getWidth()*.8&&scan.getHeight()>size.getHeight()*.8)scannedPages.add(page-1);}}pages.add(text);}
        }
        File temporary=null;int ocrPages=0;
        try {
            if(!scannedPages.isEmpty()){
                temporary=File.createTempFile("resume-ocr-",".pdf",context.getCacheDir());try(FileOutputStream out=new FileOutputStream(temporary)){out.write(bytes);}
                try(ParcelFileDescriptor descriptor=ParcelFileDescriptor.open(temporary,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(descriptor);TextRecognizer recognizer=TextRecognition.getClient(new ChineseTextRecognizerOptions.Builder().build())){
                    for(int index=0;index<pages.size();index++){
                        progress.accept("正在本机识别第 "+(index+1)+" / "+pages.size()+" 页…");if(!scannedPages.contains(index))continue;
                        try(PdfRenderer.Page page=renderer.openPage(index)){
                            double scale=Math.min(2200.0/page.getWidth(),2600.0/page.getHeight());Bitmap image=Bitmap.createBitmap(Math.max(1,(int)(page.getWidth()*scale)),Math.max(1,(int)(page.getHeight()*scale)),Bitmap.Config.ARGB_8888);
                            try {image.eraseColor(Color.WHITE);page.render(image,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);var recognized=Tasks.await(recognizer.process(InputImage.fromBitmap(image,0)),90,TimeUnit.SECONDS);pages.set(index,pages.get(index)+"\n"+recognized.getText());ocrPages++;}
                            finally {image.recycle();}
                        }
                    }
                }
            }
        }finally{if(temporary!=null&&!temporary.delete())temporary.deleteOnExit();}
        String text=String.join("\n\n",pages);if(characters(text)<10)throw new IOException("未识别到可用文字，请选择清晰、未加密的 PDF");
        return ResumeImport.parse(MobileCompat.map("sourceName",name,"text",text,"pageCount",pages.size(),"ocrPages",ocrPages));
    }
    private static long characters(String text){return text.codePoints().filter(Character::isLetterOrDigit).count();}
}
