using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private bool syncBusy;
    private TaskCompletionSource? activeSync;
    private ContentDialog? syncConflictDialog;
    private List<(Control Control,bool Enabled)> DisableSyncInputs()
    {
        var controls=new List<(Control,bool)>();
        void Visit(DependencyObject parent)
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {
                var child=VisualTreeHelper.GetChild(parent,i);
                if(child is Control control) controls.Add((control,control.IsEnabled));
                Visit(child);
            }
        }
        Visit(shell);
        foreach(var (control,_) in controls) control.IsEnabled=false;
        return controls;
    }
    private async Task FinishSyncBeforeClose()
    {
        if(activeSync is null) return;
        syncConflictDialog?.Hide();
        saveState.Text="正在完成同步，然后关闭…";
        await activeSync.Task;
    }
    private void AddSyncSettings(StackPanel panel)
    {
        SyncSettings settings;
        try {settings=SyncSettings.Load(api.DataDirectory);} catch(IOException ex) {panel.Children.Add(Card(Text(ex.Message,13)));return;}
        var content=Stack(12); content.Children.Add(Text("跨端同步 · WebDAV",20,true));
        content.Children.Add(Text("手动同步简历、内嵌照片、历史版本、职位收藏、投递记录、日程、公司来源、面试题库与掌握进度和非敏感设置。电脑和安卓填写相同实际同步目录。API Key、网站 Cookie、登录会话与同步凭据只留在本机。",13));
        var address=new TextBox {Header="WebDAV 地址",PlaceholderText="https://dav.jianguoyun.com/dav/",Text=settings.DirectoryUrl};
        content.Children.Add(Text("填写服务提供的 WebDAV 入口或已有文件夹地址，首次同步自动创建并使用其下的 ResumeStudio 文件夹；已有实际同步目录继续使用。旧坚果云 /dav/ 入口会改为专用目录，目录变化时需重新填写密码。坚果云可直接填 https://dav.jianguoyun.com/dav/，用户名用坚果云账号，密码用第三方应用密码。",12));
        var username=new TextBox {Header="用户名",Text=settings.Username};
        var password=new PasswordBox {Header="密码 / 应用密码（留空保留该目标已保存密码）"};
        content.Children.Add(address);content.Children.Add(username);content.Children.Add(password);
        var status=Text("同步包：resume-studio.sync.json · 目前仅 WebDAV，GitHub 后端接口已预留。",12);content.Children.Add(status);
        async Task<SyncSettings> Configure()
        {
            var next=new SyncSettings {DirectoryUrl=SyncWebDav.SyncDirectory(address.Text,settings.DirectoryUrl).AbsoluteUri,Username=username.Text.Trim(),DeviceId=settings.DeviceId};
            bool same=next.DirectoryUrl==settings.DirectoryUrl&&next.Username==settings.Username;
            next.ProtectedPassword=same?settings.ProtectedPassword:"";
            await next.SaveAsync(api.DataDirectory,password.Password.Length>0?password.Password:null);
            password.Password="";address.Text=next.DirectoryUrl;settings=next;return next;
        }
        var actions=Row(8);
        actions.Children.Add(Button("保存同步配置",async ()=> {await Configure();status.Text="已保存本机配置，密码按当前 Windows 用户加密。尚未执行同步。";}));
        actions.Children.Add(Button("检查并同步",async ()=>
        {
            if(syncBusy) return;
            syncBusy=true; activeSync=new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool remindersWereRunning=reminderTimer.IsEnabled;
            List<(Control Control,bool Enabled)> blockedInputs=[];
            try
            {
                blockedInputs=DisableSyncInputs();
                var connection=await Configure();
                await SaveCurrent(); await preferences.SaveAsync(api.DataDirectory); editTimer.Stop();previewTimer.Stop();reminderTimer.Stop();CancelJobSearch();
                shell.IsHitTestVisible=false; status.Text="正在检查本机与云端资料…";
                using var dav=new SyncWebDav(new Uri(connection.DirectoryUrl),connection.Username,await connection.PasswordAsync());
                var coordinator=new SyncCoordinator(new SyncBackendStore(api),dav,api.DataDirectory,connection.Target,connection.DeviceId);
                var plan=await coordinator.InspectAsync(); SyncAction? direction=null;
                if(dav.UsesContentHash) status.Text="云端缺少强 ETag，正在使用内容校验；请避免多台设备同时同步。";
                if(closeRequested) return;
                if(plan.Action==SyncAction.Conflict)
                {
                    var localCount=SyncCount(plan.Local.Snapshot); var remoteCount=plan.RemoteEnvelope is null?"云端同步包不存在":SyncCount(SyncProtocol.Snapshot(plan.RemoteEnvelope,plan.Local.Snapshot));
                    var dialog=new ContentDialog
                    {
                        XamlRoot=shell.XamlRoot,Title="同步需要选择保留哪一端",CloseButtonText="取消",PrimaryButtonText="用本机替换云端",
                        SecondaryButtonText=plan.RemoteEnvelope is null?"":"用云端替换本机",DefaultButton=ContentDialogButton.Close,
                        Content=Text($"首次关联不同资料，或双方在上次同步后都有变化。\n本机：{localCount}\n云端：{remoteCount}\n选择会替换目标端的整套资料，并传播删除。导入本机前会保存完整恢复备份；请先核对方向。",14)
                    };
                    syncConflictDialog=dialog;
                    var choice=await dialog.ShowAsync(); syncConflictDialog=null;
                    if(choice==ContentDialogResult.None||closeRequested) {status.Text="同步已取消，资料未改变。";return;}
                    direction=choice==ContentDialogResult.Primary?SyncAction.Push:SyncAction.Pull;
                }
                var action=await coordinator.ExecuteAsync(plan,direction);
                if(action==SyncAction.Pull)
                {
                    current=null;dirty=false;shownCompanyJobs=null;companySearch=null;preferences=AppPreferences.Load(api.DataDirectory);
                    await DetachPreviewAsync(true);BuildShell();await Navigate("settings");
                }
                string message=action switch {SyncAction.Push=>"本机资料已上传，云端内容回读验证通过。",SyncAction.Pull=>"云端资料已导入；替换前备份保存在数据目录 sync-backups。",_=>"本机与云端资料一致，已建立同步基线。"};
                if(dav.UsesContentHash) message+=" 当前使用内容校验，无法完全阻止同时上传造成的覆盖；请同一时间只在一台设备同步。";
                status.Text=message;Success("同步完成",message);
            }
            catch(Exception ex) {status.Text="同步未完成："+ex.Message;throw;}
            finally {foreach(var (control,enabled) in blockedInputs) control.IsEnabled=enabled;shell.IsHitTestVisible=true;syncBusy=false;syncConflictDialog=null;if(remindersWereRunning&&!closeRequested) reminderTimer.Start();activeSync?.TrySetResult();activeSync=null;}
        },true));
        actions.Children.Add(Button("清除本机同步密码",async ()=> {settings.ProtectedPassword="";await settings.SaveAsync(api.DataDirectory,"");password.Password="";status.Text="已清除本机保存密码。";}));
        content.Children.Add(actions); content.Children.Add(Text("双方仅一端变化时按基线自动选择方向；双方变化时由你选择。支持强 ETag 时使用条件写入；缺少强 ETag 时使用内容校验，请同一时间只在一台设备同步。同步包以明文保存于你选择的 WebDAV，包含个人简历信息；请使用个人目录。每次下载替换前保留本机整包备份，备份需自行管理。",12));
        panel.Children.Add(Card(content));
    }
    private static string SyncCount(System.Text.Json.Nodes.JsonObject snapshot)=>$"{snapshot["resumes"]!.AsArray().Count} 份简历 / {snapshot["favorites"]!.AsArray().Count} 个收藏 / {snapshot["applications"]!.AsArray().Count} 条投递 / {snapshot["schedule"]!.AsArray().Count} 条日程 / {snapshot["interviews"]?.AsArray().Count??0} 个题库";
}
