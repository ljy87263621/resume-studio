using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using ResumeStudio.Services;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task VerifyPublishedUpdateChannels(string output,List<string> checks)
    {
        if(Environment.GetEnvironmentVariable("RESUMESTUDIO_LIVE_UPDATES")!="1")return;
        await Navigate("settings");await Task.Delay(200);
        var choice=FindCombo(body,"更新通道")??throw new Exception("Update channel selector missing");
        choice.SelectedIndex=1;await Task.Delay(200);
        void InvokeCheck(){var button=FeatureButton(body,"检查更新")??throw new Exception("Check button missing");((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();}
        InvokeCheck();await Task.Delay(100);
        for(int i=0;i<800&&updateBusy;i++)await Task.Delay(50);
        string expected=Environment.GetEnvironmentVariable("RESUMESTUDIO_EXPECTED_COMMIT")??"";
        if(updateBusy||availableUpdate is null||availableUpdate.Identity.Channel!="nightly"||expected.Length>0&&availableUpdate.Identity.Commit!=expected||installUpdateButton?.IsEnabled!=true)throw new Exception("Native Nightly check failed: "+updateStatus?.Text);
        checks.Add("actual native update button discovers the published signed Nightly channel and enables download");
        await SaveNativePreview(Path.Combine(output,"nightly-update-ready.png"));
        choice.SelectedIndex=0;await Task.Delay(200);InvokeCheck();await Task.Delay(100);
        for(int i=0;i<800&&updateBusy;i++)await Task.Delay(50);
        if(updateBusy||!updateStatus!.Text.Contains("暂无可用")||availableUpdate is not null||installUpdateButton?.IsEnabled!=false)throw new Exception("Native Stable channel should be current: "+updateStatus?.Text);
        checks.Add("native Stable check accepts its signed channel and correctly recognizes the current version");
    }
}
