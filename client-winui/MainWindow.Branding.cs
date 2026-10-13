using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static Image BrandLogo(double size)
    {
        var image=new Image {Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center,Source=new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","AppLogo.png")))};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image,"拾简软件标志");
        return image;
    }
}
