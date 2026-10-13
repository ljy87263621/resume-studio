param([Parameter(Mandatory)][string]$SetupPath,[string]$SourceDirectory='release/ResumeStudio')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Production installer acceptance is restricted to disposable GitHub Windows runners; it must not replace a local installation'}
$source=[IO.Path]::GetFullPath($SourceDirectory)
$setup=[IO.Path]::GetFullPath($SetupPath)
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('ResumeInstaller-'+[guid]::NewGuid().ToString('N'))
$installRoot=Join-Path $testRoot 'installed'
New-Item -ItemType Directory -Path $testRoot -Force|Out-Null
if($env:GITHUB_ENV){Add-Content $env:GITHUB_ENV "RESUMESTUDIO_INSTALLER_TEST_OUTPUT=$testRoot"}
$desktopLink=Join-Path ([Environment]::GetFolderPath('Desktop')) '拾简 - 简历制作与投递.lnk'
$menuLink=Join-Path ([Environment]::GetFolderPath('Programs')) '拾简 Resume Studio.lnk'
if((Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $menuLink)){throw 'Installer runner already has application shortcuts'}
$dataRoot=Join-Path $env:LOCALAPPDATA 'ResumeStudio'
if((Test-Path -LiteralPath $dataRoot) -and @(Get-ChildItem -LiteralPath $dataRoot -Force).Count){throw 'Installer runner already has personal app data'}
New-Item -ItemType Directory -Path $dataRoot -Force|Out-Null
$data=Join-Path $dataRoot 'installer-preservation-fixture.json'
[IO.File]::WriteAllText($data,'{"resume":"personal data must survive setup and uninstall"}')
$dataHash=(Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash
$checks=[Collections.Generic.List[string]]::new()
function Assert($condition,[string]$label){if(!$condition){throw "Installer acceptance failed: $label"};$checks.Add($label);Write-Output "PASS: $label"}
function Run-Setup([string]$file,[string[]]$arguments){
    $info=[Diagnostics.ProcessStartInfo]::new($file);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$testRoot
    foreach($argument in $arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info)
    if(!$process.WaitForExit(240000)){throw 'Installer process timed out'}
    if($process.ExitCode -ne 0){throw "Installer exit code $($process.ExitCode); see $testRoot"}
}
Run-Setup $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR='+$installRoot),('/LOG='+ (Join-Path $testRoot 'install.log')))
$exe=Join-Path $installRoot 'ResumeStudio.exe'
Assert (Test-Path -LiteralPath $exe) 'production Setup installs to the requested current-user location'
$manifest=Get-Content (Join-Path $source 'distribution-manifest.json') -Raw|ConvertFrom-Json
foreach($file in $manifest.files){
    $installed=[IO.Path]::GetFullPath((Join-Path $installRoot $file.path))
    if(!$installed.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest left installer fixture'}
    if((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne $file.sha256){throw "Installed payload hash mismatch: $($file.path)"}
}
Assert $true 'every installed manifest-owned file matches the release payload'
# Inno may serialize icons using Windows 8.3 paths. Compare the resolved file,
# rather than treating an equivalent short path as a broken shortcut.
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
class ResumeInstallerShellLink {}
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ResumeInstallerShellLinkW {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr findData, uint flags);
    void GetIDList(out IntPtr pidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int command);
    void SetShowCmd(int command);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
}
public static class ResumeInstallerPaths {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern uint GetLongPathName(string path, StringBuilder buffer, uint size);
    public static string Resolve(string path) {
        var buffer=new StringBuilder(32768);
        uint length=GetLongPathName(path,buffer,(uint)buffer.Capacity);
        if(length==0 || length>=buffer.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return buffer.ToString().TrimEnd('\\');
    }
    public static string[] ReadShortcut(string path) {
        object instance=new ResumeInstallerShellLink();
        try {
            ((IPersistFile)instance).Load(path,0);
            var link=(ResumeInstallerShellLinkW)instance;
            var target=new StringBuilder(32768);
            var working=new StringBuilder(32768);
            var icon=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,0);
            link.GetWorkingDirectory(working,working.Capacity);
            link.GetIconLocation(icon,icon.Capacity,out int index);
            return new[]{target.ToString(),working.ToString(),icon.ToString()};
        } finally {Marshal.FinalReleaseComObject(instance);}
    }
}
'@
foreach($path in @($desktopLink,$menuLink)){
    Assert (Test-Path -LiteralPath $path) "automatic shortcut exists: $([IO.Path]::GetFileName($path))"
    $details=[ResumeInstallerPaths]::ReadShortcut($path)
    Write-Output ("Shortcut details: "+(@{target=$details[0];workingDirectory=$details[1];icon=$details[2];size=(Get-Item -LiteralPath $path).Length}|ConvertTo-Json -Compress))
    Assert ([ResumeInstallerPaths]::Resolve($details[0]) -eq [ResumeInstallerPaths]::Resolve($exe) -and [ResumeInstallerPaths]::Resolve($details[1]) -eq [ResumeInstallerPaths]::Resolve($installRoot) -and [ResumeInstallerPaths]::Resolve($details[2]) -eq [ResumeInstallerPaths]::Resolve((Join-Path $installRoot 'Assets/AppIcon.ico'))) 'shortcut uses the installed executable, working directory and app icon'
}
$registryPath='Software\Microsoft\Windows\CurrentVersion\Uninstall\{B782F0B7-68E7-4D53-9200-5A45126A71F5}_is1'
$registered=$false
foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
    $hive=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view);$entry=$hive.OpenSubKey($registryPath)
    try {if($entry -and [string]$entry.GetValue('InstallLocation') -like ($installRoot+'*')){$registered=$true}}finally{if($entry){$entry.Dispose()};$hive.Dispose()}
}
Assert $registered 'installer registers a per-user Windows uninstall entry'
Assert ((Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash -eq $dataHash) 'first installation preserves existing personal data'
$note=Join-Path $installRoot 'personal-note.txt';[IO.File]::WriteAllText($note,'unknown file must be retained')
Run-Setup $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR='+$installRoot),('/LOG='+ (Join-Path $testRoot 'reinstall.log')))
Assert ((Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash -eq $dataHash -and (Test-Path -LiteralPath $note)) 'repeat installation preserves personal data and unknown files'
& (Join-Path $installRoot 'runtime/bin/java.exe') --version
Assert ($LASTEXITCODE -eq 0) 'installed bundled Java runtime starts successfully'
$uninstaller=Join-Path $installRoot 'unins000.exe'
if(![IO.Path]::GetFullPath($uninstaller).StartsWith($testRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe test uninstall target'}
Run-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+ (Join-Path $testRoot 'uninstall.log')))
Assert (!(Test-Path -LiteralPath $exe) -and !(Test-Path -LiteralPath $desktopLink) -and !(Test-Path -LiteralPath $menuLink)) 'uninstall removes owned program files and both shortcuts'
Assert ((Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash -eq $dataHash -and (Test-Path -LiteralPath $note)) 'uninstall retains personal records and unknown application-directory files'
@{status='passed';checks=$checks;root=$testRoot;setup=$setup}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $testRoot 'result.json') -Encoding utf8
if($env:GITHUB_ENV){Add-Content $env:GITHUB_ENV "RESUMESTUDIO_INSTALLER_TEST_OUTPUT=$testRoot"}
exit 0
