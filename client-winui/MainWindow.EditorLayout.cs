using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private Grid? editorWorkspace,editorViewBar;
    private FrameworkElement? editorDirectory,editorPreviewFrame;
    private ComboBox? editorView,compactModulePicker;
    private bool updatingCompactModulePicker;
    private void RefreshCompactModulePicker()
    {
        if(current is null||compactModulePicker is null)return;
        updatingCompactModulePicker=true;
        try {
            var titles=new[] {"基本信息"}.Concat(current.Sections.Select(s=>s.Title)).ToList();
            if(!compactModulePicker.Items.Cast<string>().SequenceEqual(titles))compactModulePicker.ItemsSource=titles;
            compactModulePicker.SelectedIndex=Math.Clamp(selectedEditorPart>=2?selectedEditorPart-1:0,0,current.Sections.Count);
        }
        finally {updatingCompactModulePicker=false;}
    }
    private void UpdateWorkbenchLayout()
    {
        if(editorWorkspace is null||editorFields is null||editorPreviewFrame is null||editorDirectory is null)return;
        double width=editorWorkspace.ActualWidth;bool wide=width>=1100,compact=width<650;
        editorWorkspace.ColumnDefinitions[0].Width=new GridLength(compact?0:156);
        editorWorkspace.ColumnDefinitions[1].Width=new GridLength(compact?0:12);
        editorWorkspace.ColumnDefinitions[3].Width=new GridLength(wide?12:0);
        editorWorkspace.ColumnDefinitions[4].Width=new GridLength(wide?360:0);
        editorDirectory.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        editorViewBar!.Visibility=wide?Visibility.Collapsed:Visibility.Visible;
        compactModulePicker!.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumn(editorFields,wide?4:2);Grid.SetColumnSpan(editorFields,wide?1:3);
        Grid.SetColumnSpan(editorPreviewFrame,wide?1:3);
        editorFields.Visibility=wide||editorView!.SelectedIndex!=1?Visibility.Visible:Visibility.Collapsed;
        editorPreviewFrame.Visibility=wide||editorView!.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
    }
    private async Task UpdatePreviewScale()
    {
        if(currentPage!="editor"||preview?.CoreWebView2 is null)return;
        await preview.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.setProperty('--preview-scale',Math.min(1,Math.max(.1,(innerWidth-24)/(210*96/25.4))))");
    }
}
