using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    // Share materials across cards rather than creating a blur brush for every list item.
    private static Brush panelMaterial = new SolidColorBrush(Colors.White);
    private static Brush edgeMaterial = new SolidColorBrush(Colors.Transparent);
    private static Brush PanelMaterial => haruStyle ? panelMaterial : Brush(Surface);
    private static Brush EdgeMaterial => haruStyle ? edgeMaterial : Brush(Stroke);
    private bool nativeGlass;

    private void ConfigureWindowMaterial()
    {
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool acrylic=haruStyle&&Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
        SystemBackdrop=acrylic?new DesktopAcrylicBackdrop():new MicaBackdrop();
        nativeGlass=haruStyle&&NativeWindowGlass.Enable(handle,darkStyle);
        if(!haruStyle) NativeWindowGlass.Disable(handle);
        if (!haruStyle) { shell.Background=Brush("#F5F7FB"); return; }
        // Transparent hosts are essential: opaque NavigationView/card backgrounds cover the DWM acrylic.
        shell.Background=Brush(acrylic?(darkStyle?"#18191B25":"#14F7F7FA"):(darkStyle?"#252732":"#F7F7FA"));
        // One real window blur, with translucent tint on top. Per-card AcrylicBrush can fall back to
        // an opaque fill in desktop windows, hiding the glass; layering tints also avoids repeated blur work.
        panelMaterial=new LinearGradientBrush {
            StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(0,1),
            GradientStops={
                new GradientStop {Color=((SolidColorBrush)Brush(darkStyle?"#601F2230":"#96FFFFFF")).Color,Offset=0},
                new GradientStop {Color=((SolidColorBrush)Brush(darkStyle?"#38232634":"#60F5F6FB")).Color,Offset=1}
            }
        };
        edgeMaterial=new LinearGradientBrush {
            StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(0.6,1),
            GradientStops={
                new GradientStop {Color=((SolidColorBrush)Brush(darkStyle?"#66FFFFFF":"#D9FFFFFF")).Color,Offset=0},
                new GradientStop {Color=((SolidColorBrush)Brush(darkStyle?"#1FFFFFFF":"#55FFFFFF")).Color,Offset=1}
            }
        };
    }

    private void ConfigureGlassNavigation(NavigationView nav)
    {
        foreach(string key in new[] {"NavigationViewContentBackground","NavigationViewExpandedPaneBackground","NavigationViewTopPaneBackground"})
            nav.Resources[key]=new SolidColorBrush(Colors.Transparent);
        nav.Resources["NavigationViewDefaultPaneBackground"]=panelMaterial;
        nav.Resources["NavigationViewContentGridBorderBrush"]=edgeMaterial;
        nav.Loaded+=(_,_)=>ApplyGlassHosts(nav);
        nav.PaneOpened+=(_,_)=>ApplyGlassHosts(nav);
        nav.DisplayModeChanged+=(_,_)=>ApplyGlassHosts(nav);
    }

    private void ConfigureGlassTitleBar()
    {
        ExtendsContentIntoTitleBar=haruStyle;
        SetTitleBar(haruStyle?headerFrame:null);
        headerFrame!.Margin=new Thickness(0,haruStyle?10:0,haruStyle?138:0,20);
        if(Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar=AppWindow.TitleBar;
            if(haruStyle)
            {
                titleBar.ButtonBackgroundColor=Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor=Colors.Transparent;
                titleBar.ButtonForegroundColor=((SolidColorBrush)Brush(Ink)).Color;
                titleBar.ButtonInactiveForegroundColor=((SolidColorBrush)Brush(darkStyle?"#ADB6CC":"#697086")).Color;
            }
            else { titleBar.ButtonBackgroundColor=null; titleBar.ButtonInactiveBackgroundColor=null; titleBar.ButtonForegroundColor=null; titleBar.ButtonInactiveForegroundColor=null; }
        }
    }

    private static void ApplyGlassHosts(DependencyObject parent)
    {
        // Set actual template hosts too: visual states can override the default resource setter.
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is Grid grid && grid.Name=="ContentGrid") grid.Background=new SolidColorBrush(Colors.Transparent);
            if(child is SplitView split && split.Pane is Panel pane) pane.Background=panelMaterial;
            ApplyGlassHosts(child);
        }
    }
}
