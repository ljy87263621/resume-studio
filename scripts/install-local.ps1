$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Join-Path $root 'release\ResumeStudio'
$destination=Join-Path $env:LOCALAPPDATA 'Programs\ResumeStudio'
if(!(Test-Path (Join-Path $source 'ResumeStudio.exe'))) { throw 'Build the application first' }
$running=@(Get-Process ResumeStudio -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $destination 'ResumeStudio.exe') })
if($running.Count -gt 0) { throw 'Close the installed application before updating' }
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
$exe=Join-Path $destination 'ResumeStudio.exe'
if((Get-FileHash $exe -Algorithm SHA256).Hash -ne (Get-FileHash (Join-Path $source 'ResumeStudio.exe') -Algorithm SHA256).Hash) { throw 'Installed EXE verification failed' }
$desktop=[Environment]::GetFolderPath('Desktop')
$shortcutPath=Join-Path $desktop '拾简 - 简历制作与投递.lnk'
if(Test-Path $shortcutPath) {
    $existing=(New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    if($existing.TargetPath -ne $exe) { throw 'A different shortcut already uses this name' }
}
$shortcut=(New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath=$exe; $shortcut.WorkingDirectory=$destination; $shortcut.Description='Java 后端 + WinUI 3 简历制作与招聘投递客户端'; $shortcut.Save()
Write-Output "Installed: $exe"
Write-Output "Desktop shortcut: $shortcutPath"
