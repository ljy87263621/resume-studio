param([int]$ProcessId,[string]$OutputPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CaptureWindow {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle,IntPtr dc,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr handle);
}
'@
$proc=Get-Process -Id $ProcessId
if([CaptureWindow]::IsIconic($proc.MainWindowHandle)) { throw 'The window is minimized; preserve any previous screenshot.' }
$previous=[CaptureWindow]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$rect=[CaptureWindow+RECT]::new()
if(![CaptureWindow]::GetWindowRect($proc.MainWindowHandle,[ref]$rect)) { throw 'Window not found' }
$bitmap=[System.Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
$dc=$graphics.GetHdc()
try { $ok=[CaptureWindow]::PrintWindow($proc.MainWindowHandle,$dc,2) } finally { $graphics.ReleaseHdc($dc) }
if(!$ok) { throw 'Window capture failed' }
$bitmap.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose(); $bitmap.Dispose()
$null=[CaptureWindow]::SetThreadDpiAwarenessContext($previous)
Write-Output $OutputPath
