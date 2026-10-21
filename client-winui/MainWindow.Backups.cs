using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;
using Windows.Storage.Pickers;
namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task ImportBackupFile(){var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");InitPicker(picker);var file=await picker.PickSingleFileAsync();if(file is null)return;if(new FileInfo(file.Path).Length>64*1024*1024+8192)throw new IOException("备份超过 64 MiB");var bytes=await File.ReadAllBytesAsync(file.Path);await api.Post<JsonElement>("backups",new {operation="import",base64=Convert.ToBase64String(bytes)});await ShowBackupCatalog();}
    private async Task ShowBackupCatalog()
    {
        await SaveCurrent();var records=await api.Post<JsonElement>("backups",new {operation="list"});var rows=records.EnumerateArray().Select(r=>r.Clone()).ToList();
        if(rows.Count==0){Success("尚无备份","下载同步资料、合并或恢复前会自动生成备份。");return;}
        var choice=new ComboBox {Header="备份时间／来源／数量",ItemsSource=rows.Select(r=>r.GetProperty("at").GetString()+" · "+r.GetProperty("source").GetString()+" · "+r.GetProperty("device").GetString()+" · "+r.GetProperty("counts").GetRawText()).ToList(),SelectedIndex=0,HorizontalAlignment=Microsoft.UI.Xaml.HorizontalAlignment.Stretch};
        var selection=await new ContentDialog {XamlRoot=shell.XamlRoot,Title="备份与恢复",Content=choice,PrimaryButtonText="预览与恢复",SecondaryButtonText="导出 JSON",CloseButtonText="取消"}.ShowAsync();if(selection==ContentDialogResult.None)return;
        string name=rows[choice.SelectedIndex].GetProperty("name").GetString()!;
        if(selection==ContentDialogResult.Secondary){var data=await api.Post<JsonElement>("backups",new {operation="json",name});var picker=new FileSavePicker {SuggestedFileName="拾简备份"};picker.FileTypeChoices.Add("备份 JSON",new[]{".json"});InitPicker(picker);var file=await picker.PickSaveFileAsync();if(file is not null)await File.WriteAllTextAsync(file.Path,data.GetProperty("json").GetString());return;}
        var preview=await api.Post<JsonElement>("backups",new {operation="preview",name});var items=preview.GetProperty("records").EnumerateArray().Select(r=>r.Clone()).ToList();
        if(items.Count==0){
            if(!await Confirm("恢复空记录备份？","这份备份没有资料记录。整套恢复会清空本机资料并恢复备份中的设置；执行前自动备份。"))return;
            await api.Post<JsonElement>("backups",new {operation="restore",name,expectedHash=preview.GetProperty("expectedHash").GetString()});current=null;dirty=false;await DetachPreviewAsync(true);preferences=AppPreferences.Load(api.DataDirectory);await RefreshPage(currentPage);Success("恢复完成","原资料已备份，请重新检查同步。");return;
        }
        var list=new ComboBox {ItemsSource=items.Select(r=>r.GetProperty("kind").GetString()+" · "+r.GetProperty("title").ToString()).ToList(),SelectedIndex=0,HorizontalAlignment=Microsoft.UI.Xaml.HorizontalAlignment.Stretch};
        var content=new TextBox {Text=items[0].GetProperty("content").GetString(),IsReadOnly=true,AcceptsReturn=true,TextWrapping=Microsoft.UI.Xaml.TextWrapping.Wrap,Height=250};list.SelectionChanged+=(_,_)=>{if(list.SelectedIndex>=0)content.Text=items[list.SelectedIndex].GetProperty("content").GetString();};var panel=Stack(10);panel.Children.Add(list);panel.Children.Add(content);
        var action=await new ContentDialog {XamlRoot=shell.XamlRoot,Title="恢复前预览",Content=panel,PrimaryButtonText="仅恢复选中记录",SecondaryButtonText="恢复整套备份",CloseButtonText="取消"}.ShowAsync();if(action==ContentDialogResult.None)return;
        if(!await Confirm("确认恢复？",action==ContentDialogResult.Primary?"只替换选中记录，其他资料保留。恢复前自动备份。":"将替换本机整套资料，恢复前自动备份。"))return;
        var selected=items[list.SelectedIndex];await api.Post<JsonElement>("backups",new {operation=action==ContentDialogResult.Primary?"restoreOne":"restore",name,kind=selected.GetProperty("kind").GetString(),id=selected.GetProperty("id").GetString(),expectedHash=preview.GetProperty("expectedHash").GetString()});current=null;dirty=false;await DetachPreviewAsync(true);preferences=AppPreferences.Load(api.DataDirectory);await RefreshPage(currentPage);Success("恢复完成","原资料已备份，请重新检查同步。");
    }
}
