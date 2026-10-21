using System.Text.Json;
using Windows.Storage.Pickers;
using ResumeStudio.Services;
namespace ResumeStudio;
public sealed partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern nint GetLastActivePopup(nint owner);
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool PostMessage(nint window,uint message,nint wparam,nint lparam);
    private static Task SubmitPicker(nint owner)=>Task.Run(async ()=>{for(int i=0;i<50;i++){await Task.Delay(100);nint popup=GetLastActivePopup(owner);GetWindowThreadProcessId(popup,out uint process);if(popup!=owner&&process==Environment.ProcessId){await Task.Delay(700);for(int attempt=0;attempt<10;attempt++){if(GetLastActivePopup(owner)!=popup)return;PostMessage(popup,0x111,1,0);await Task.Delay(300);}return;}}throw new IOException("验证窗口未显示。");});
    private async Task PickerTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??api.DataDirectory;
        Directory.CreateDirectory(output);var results=new List<object>();
        foreach(string kind in new[]{"open-json","open-photo","save-json"})
        {
            string stage="construct";
            try
            {
                stage="desktop-dialog";using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(1));
                nint owner=WinRT.Interop.WindowNative.GetWindowHandle(this);
                string? choice=kind=="save-json"?DesktopFileDialog.Save(owner,"导出 JSON 测试",[".json"],"证件照简历测试",cancel.Token):DesktopFileDialog.Open(owner,"打开文件测试",kind=="open-photo"?[".png",".jpg",".jpeg"]:[".json"],cancel.Token);
                if(choice is not null)throw new IOException("取消测试不应选定文件。 ");
                results.Add(new {kind,status="passed",stage});
            }
            catch(Exception ex){results.Add(new {kind,status="failed",stage,type=ex.GetType().FullName,hresult=$"0x{ex.HResult:X8}",error=ex.ToString()});}
            await File.WriteAllTextAsync(Path.Combine(output,"picker-result.json"),JsonSerializer.Serialize(results,BackendClient.Json));
        }
        try
        {
            string fixture=Environment.GetEnvironmentVariable("RESUMESTUDIO_RESUME_FIXTURE")??"";
            if(File.Exists(fixture))
            {
                await ImportResumeFile(fixture);await WaitForPreview();
                string expected=JsonSerializer.Serialize(current,BackendClient.Json);string exported=Path.Combine(output,"嵌入式简历-往返.json");await ExportResumeFile(exported);
                if(await File.ReadAllTextAsync(exported)!=expected)throw new IOException("JSON 导出内容不一致。");
                string previousId=current!.Id;await ImportResumeFile(exported);await WaitForPreview();
                if(current!.Id==previousId||current.Profile.Name is not {Length:>0})throw new IOException("JSON 导入未创建独立简历。");
                string photo=Path.Combine(output,"证件照.png");byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
                // Padded source exercises the former 600 KB rejection without changing the valid image.
                await File.WriteAllBytesAsync(photo,png.Concat(new byte[650000]).ToArray());await ImportPhotoFile(photo);
                var saved=(await api.Get<List<Resume>>("resumes")).Single(r=>r.Id==current.Id);
                if(!saved.Photo.StartsWith("data:image/jpeg;base64,"))throw new IOException("照片未持久保存。");
                await ExportResumeFile(Path.Combine(output,"嵌入式简历-含测试照片.json"));
                var original=current;current=CloneResume(original);current.Id="different-resume";current.Photo="";bool refused=false;
                try{ApplyImportedPhoto(original,original.Id,"wrong-photo");}catch(IOException){refused=true;}
                if(!refused||current.Photo!="")throw new IOException("切换简历后不应提交旧照片。");current=original;
                foreach(var input in new[]{(path:exported,extensions:new[]{".json"}),(path:photo,extensions:new[]{".png"})})
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));nint owner=WinRT.Interop.WindowNative.GetWindowHandle(this);
                    var submit=SubmitPicker(owner);string? selected=DesktopFileDialog.Open(owner,"拾简文件验证",input.extensions,timeout.Token,input.path);await submit;
                    if(selected is null||Path.GetFullPath(selected)!=Path.GetFullPath(input.path))throw new IOException($"打开窗口没有返回选定文件：{selected??"<null>"}；预期：{input.path}");
                }
                using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    nint owner=WinRT.Interop.WindowNative.GetWindowHandle(this);var submit=SubmitPicker(owner);string? selected=DesktopFileDialog.Save(owner,"拾简文件验证",[".json"],Path.Combine(output,"窗口导出无扩展"),timeout.Token);await submit;
                    if(selected is null||!selected.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new IOException("保存窗口没有补全 JSON 扩展名。");await ExportResumeFile(selected);
                }
                results.Add(new {kind="resume-roundtrip",status="passed",stage="JSON export/import and photo resize/persistence"});
            }
        }
        catch(Exception ex){results.Add(new {kind="resume-roundtrip",status="failed",stage="file-processing",error=ex.ToString()});}
        await File.WriteAllTextAsync(Path.Combine(output,"picker-result.json"),JsonSerializer.Serialize(results,BackendClient.Json));
        closing=true;api.Dispose();Close();
    }
}
