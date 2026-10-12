using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly Stopwatch sessionClock = Stopwatch.StartNew();
    private readonly Dictionary<string,double> timings = [];
    private int webInitializations, previewNavigations, previewPatches;
    private void Mark(string name) => timings[name] = Math.Round(sessionClock.Elapsed.TotalMilliseconds,1);

    private static TextBox? FindTextField(DependencyObject parent, string header)
    {
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if (child is TextBox box && box.Header?.ToString()==header) return box;
            var found=FindTextField(child,header); if(found is not null) return found;
        }
        return null;
    }
    private async Task Benchmark()
    {
        var output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"benchmark");
        Directory.CreateDirectory(output);
        try
        {
            current=await api.Save("resumes",Demo(templates[0])); dirty=false;
            var watch=Stopwatch.StartNew(); await OpenEditor(); await WaitForPreview();
            double firstEditor=watch.Elapsed.TotalMilliseconds;
            var summary=FindTextField(body,"个人简介")??throw new Exception("Editor field missing");
            var edits=Stopwatch.StartNew();
            for(int i=0;i<30;i++) { summary.Text="性能测试，仅用于隔离验证。"+new string('字',i*8); await Task.Delay(20); }
            double editDuration=edits.Elapsed.TotalMilliseconds;
            for(int i=0;i<100&&dirty;i++) await Task.Delay(50);
            if(dirty) throw new Exception("Autosave did not complete");
            watch.Restart(); await OpenEditor(); await WaitForPreview();
            double repeatEditor=watch.Elapsed.TotalMilliseconds;
            await File.WriteAllTextAsync(Path.Combine(output,"performance.json"),JsonSerializer.Serialize(new {
                status="passed",startup=timings,firstEditorMs=Math.Round(firstEditor,1),repeatEditorMs=Math.Round(repeatEditor,1),
                editBurstMs=Math.Round(editDuration,1),webInitializations,previewNavigations,previewPatches,
                privateMemoryMb=Math.Round(Process.GetCurrentProcess().PrivateMemorySize64/1024d/1024d,1)
            },Services.BackendClient.Json));
        }
        catch(Exception ex) { await File.WriteAllTextAsync(Path.Combine(output,"performance.json"),JsonSerializer.Serialize(new {status="failed",error=ex.ToString()},Services.BackendClient.Json)); }
    }
}
