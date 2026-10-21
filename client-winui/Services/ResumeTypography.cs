using Microsoft.UI.Xaml.Media;
namespace ResumeStudio.Services;
public static class ResumeTypography
{
    public static FontFamily InterfaceFont {get;}=new("ms-appx:///Assets/Fonts/MiSans-Regular.ttf#MiSans");
    public static readonly string[] Names=ResumeLayout.FontNames;
    public static readonly string[] Labels=["MiSans · 默认","微软雅黑","宋体","Arial","Times New Roman"];
    public static string Normalize(string? name)=>ResumeLayout.Font(name);
}
