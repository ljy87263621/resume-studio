using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ResumeStudio.Services;

public sealed record PdfPagePreview(uint Number,bool NearlyBlank,byte[] Image);
public static class PdfInspection
{
    public static async Task<List<PdfPagePreview>> Read(string path)
    {
        var pdf=await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        if(pdf.PageCount>60) throw new IOException("简历超过 60 页，请检查内容后导出。");
        var result=new List<PdfPagePreview>();
        for(uint i=0;i<pdf.PageCount;i++) {
            using var page=pdf.GetPage(i); using var stream=new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream,new PdfPageRenderOptions {DestinationWidth=420}); stream.Seek(0);
            var decoder=await BitmapDecoder.CreateAsync(stream); var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
            var pixels=data.DetachPixelData(); int marks=0;
            for(int p=0;p+3<pixels.Length;p+=4) if(pixels[p]<235||pixels[p+1]<235||pixels[p+2]<235) marks++;
            stream.Seek(0); using var reader=new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size); var bytes=new byte[(int)stream.Size]; reader.ReadBytes(bytes);
            result.Add(new(i+1,marks<10,bytes));
        }
        return result;
    }
}
