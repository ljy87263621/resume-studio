using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ResumeStudio;

public sealed class JobCardRenderer
{
    internal Func<Job,UIElement> Render {get;}
    internal JobCardRenderer(Func<Job,UIElement> render)=>Render=render;
}

/// <summary>A native item template that creates only visible cards and refreshes when containers are recycled.</summary>
public sealed class JobCardPresenter : ContentControl
{
    public JobCardPresenter()
    {
        HorizontalContentAlignment=HorizontalAlignment.Stretch;
        VerticalContentAlignment=VerticalAlignment.Top;
        Loaded+=(_,_)=>RefreshCard();
        DataContextChanged+=(_,_)=>RefreshCard();
        Unloaded+=(_,_)=>Content=null;
    }
    private void RefreshCard()
    {
        if(DataContext is not Job job) { Content=null; return; }
        for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent is not null;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is ListView list && list.Tag is JobCardRenderer renderer) { Content=renderer.Render(job); return; }
        }
    }
}
