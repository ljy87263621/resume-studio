using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static bool motionEnabled=true;
    private static readonly UISettings motionSettings=new();
    private readonly InfoBar feedback=new() {IsClosable=true,Margin=new Thickness(0,12,0,0)};
    private readonly DispatcherTimer feedbackTimer=new() {Interval=TimeSpan.FromSeconds(8)};
    private static bool Animate=>motionEnabled&&motionSettings.AnimationsEnabled;

    private static void ScaleTo(FrameworkElement element,float scale,bool pressed=false)
    {
        var visual=ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint=new Vector3((float)element.ActualWidth/2,(float)element.ActualHeight/2,0);
        if(!Animate) {visual.StopAnimation("Scale");visual.Scale=Vector3.One;return;}
        var animation=visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration=TimeSpan.FromMilliseconds(pressed?100:280);
        animation.InsertKeyFrame(1,new Vector3(scale,scale,1),visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.18f,.8f),new Vector2(.3f,1)));
        visual.StartAnimation("Scale",animation);
    }
    private static void ButtonMotion(ButtonBase button)
    {
        bool over=false;
        button.PointerEntered+=(_,_)=> {over=true;if(button.IsEnabled)ScaleTo(button,1.035f);};
        button.PointerExited+=(_,_)=> {over=false;ScaleTo(button,1);};
        button.PointerPressed+=(_,_)=> {if(button.IsEnabled)ScaleTo(button,.965f,true);};
        button.PointerReleased+=(_,_)=>ScaleTo(button,over&&button.IsEnabled?1.035f:1);
        button.PointerCanceled+=(_,_)=>ScaleTo(button,1);
        button.PointerCaptureLost+=(_,_)=>ScaleTo(button,1);
        button.LostFocus+=(_,_)=> {if(!over)ScaleTo(button,1);};
        button.IsEnabledChanged+=(_,_)=> {if(!button.IsEnabled)ScaleTo(button,1);};
    }
    private static Border InteractiveCard(UIElement child,Thickness padding)
    {
        var card=new Border {Child=child,Padding=padding,Background=PanelMaterial,BorderBrush=EdgeMaterial,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(haruStyle?14:12)};
        // WebView hosts and complex grids keep their geometry; simple content cards can lift subtly.
        card.PointerEntered+=(_,_)=> {card.BorderBrush=Brush(Accent);if(child is StackPanel)ScaleTo(card,1.006f);};
        card.PointerExited+=(_,_)=> {card.BorderBrush=EdgeMaterial;if(child is StackPanel)ScaleTo(card,1);};
        return card;
    }
    private static void Entrance(Panel panel)
    {
        panel.ChildrenTransitions=Animate?new TransitionCollection {new EntranceThemeTransition {IsStaggeringEnabled=false}}:new TransitionCollection();
    }
    private async Task ExecuteButton(ButtonBase button,Func<Task> action)
    {
        if(!button.IsEnabled)return;
        object? original=(button as Button)?.Content;
        button.IsEnabled=false;
        try {
            var running=action();
            if(await Task.WhenAny(running,Task.Delay(180))!=running&&button is Button regular) {
                var status=Row(8);var label=Text(original?.ToString()??"处理中",14);label.Foreground=regular.Foreground;status.Children.Add(new ProgressRing {IsActive=true,Width=18,Height=18,Foreground=regular.Foreground});status.Children.Add(label);regular.Content=status;
            }
            await running;
        }
        finally {if(button is Button regular)regular.Content=original;button.IsEnabled=true;}
    }
    private void InitializeFeedback()
    {
        feedbackTimer.Tick+=(_,_)=> {feedbackTimer.Stop();feedback.IsOpen=false;};
        feedback.Closed+=(_,_)=>feedbackTimer.Stop();
        feedback.PointerEntered+=(_,_)=>feedbackTimer.Stop();
        feedback.PointerExited+=(_,_)=> {if(feedback.IsOpen)feedbackTimer.Start();};
    }
    private void Feedback(string title,string message="",Func<Task>? undo=null)
    {
        feedbackTimer.Stop();feedback.Title=title;feedback.Message=message;feedback.Severity=InfoBarSeverity.Success;
        feedback.ActionButton=undo is null?null:Button("撤销这次操作",async ()=> {feedbackTimer.Stop();await undo();feedback.IsOpen=false;});
        feedback.IsOpen=true;feedbackTimer.Start();
    }
    private Expander Disclosure(string title,UIElement content,bool expanded=false,string? key=null)
    {
        var expander=new Expander {Header=title,Content=content,IsExpanded=expanded,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        if(key is not null)expander.Tag=key;
        ToolTipService.SetToolTip(expander,"点击展开或收起，也可以用空格键操作");
        return expander;
    }
    private void SaveIndicator(string label,bool pending=false)
    {
        saveState.Text=(pending?"◌  ":"✓  ")+label;
        saveState.Foreground=Brush(pending?"#A56A05":"#167D78");
    }
}
