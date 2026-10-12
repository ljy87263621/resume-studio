using System.Runtime.InteropServices;

namespace ResumeStudio;

/// <summary>The same DWM acrylic path used by haruphoto; unsupported systems fall back to WinUI materials.</summary>
internal static class NativeWindowGlass
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    internal static bool Enable(nint window,bool dark)
    {
        int theme=dark?1:0,acrylic=3;
        _=DwmSetWindowAttribute(window,20,ref theme,sizeof(int));
        return DwmSetWindowAttribute(window,38,ref acrylic,sizeof(int))==0;
    }
    internal static void Disable(nint window)
    {
        int none=1; _=DwmSetWindowAttribute(window,38,ref none,sizeof(int));
    }
}
