param([Parameter(Mandatory)][string]$RequestFile)
$ErrorActionPreference='Stop'
$request=Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
$install=[IO.Path]::GetFullPath($request.installRoot).TrimEnd('\')
$source=[IO.Path]::GetFullPath($request.sourceRoot).TrimEnd('\')
$workspace=[IO.Path]::GetFullPath($request.workspace).TrimEnd('\')
$parent=Split-Path $install -Parent
$candidate=Join-Path $parent ('.resumestudio-new-'+[guid]::NewGuid().ToString('N'))
$backup=Join-Path $parent ('.resumestudio-backup-'+[guid]::NewGuid().ToString('N'))
$ack=Join-Path $workspace 'startup-ready.json'
$result=Join-Path $workspace 'result.json'
$swapped=$false
$newProcess=$null
function Check-OwnedSibling([string]$path) {
    $resolved=[IO.Path]::GetFullPath($path)
    if((Split-Path $resolved -Parent) -ne $parent -or !(Split-Path $resolved -Leaf).StartsWith('.resumestudio-')){throw 'Unsafe update work directory'}
    if((Test-Path -LiteralPath $resolved) -and ((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Update directory cannot be a reparse point'}
}
function Start-App([bool]$healthCheck) {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=Join-Path $install 'ResumeStudio.exe'; $info.WorkingDirectory=$install; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    if($healthCheck){$info.EnvironmentVariables['RESUMESTUDIO_UPDATE_ACK']=$ack}
    return [Diagnostics.Process]::Start($info)
}
try {
    if($install -eq [IO.Path]::GetPathRoot($install).TrimEnd('\') -or !(Test-Path -LiteralPath (Join-Path $install 'ResumeStudio.exe')) -or !(Test-Path -LiteralPath (Join-Path $install 'distribution-manifest.json'))){throw 'Invalid application installation'}
    if(!$source.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase) -or !$workspace.StartsWith([IO.Path]::GetFullPath((Join-Path $request.dataDirectory 'updates'))+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Update workspace is outside the application data directory'}
    if((Get-Item -LiteralPath $install -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Application directory cannot be a reparse point'}
    Check-OwnedSibling $candidate; Check-OwnedSibling $backup
    if(@(Get-ChildItem -LiteralPath $install -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Application directory contains a reparse point'}
    $process=Get-Process -Id $request.processId -ErrorAction SilentlyContinue
    if($process){
        if($process.Path -ne (Join-Path $install 'ResumeStudio.exe')){throw 'Unexpected application process'}
        Wait-Process -Id $request.processId -Timeout 45 -ErrorAction SilentlyContinue
        if(Get-Process -Id $request.processId -ErrorAction SilentlyContinue){throw 'Application did not finish saving and closing'}
    }
    # Preserve unknown/modified files in a candidate copy; the original is kept until startup succeeds.
    Copy-Item -LiteralPath $install -Destination $candidate -Recurse
    & (Join-Path $PSScriptRoot 'sync-distribution.ps1') -SourceDirectory $source -DestinationDirectory $candidate
    Move-Item -LiteralPath $install -Destination $backup
    try {Move-Item -LiteralPath $candidate -Destination $install; $swapped=$true}
    catch {Move-Item -LiteralPath $backup -Destination $install; throw}
    if($request.restart){
        $newProcess=Start-App $true
        for($attempt=0;$attempt -lt 120 -and !(Test-Path -LiteralPath $ack);$attempt++){Start-Sleep -Milliseconds 500}
        if(!(Test-Path -LiteralPath $ack)){throw 'New application did not confirm successful startup'}
    }
    @{status='installed';backup=$backup;installedAt=[DateTimeOffset]::Now.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $result -Encoding UTF8
    # Keep the previous program directory for recovery. No personal document directory is changed.
}
catch {
    $message=$_.Exception.Message
    if($swapped -and (Test-Path -LiteralPath $backup)){
        if($newProcess -and !$newProcess.HasExited){$null=$newProcess.CloseMainWindow(); $newProcess.WaitForExit(10000) | Out-Null; if(!$newProcess.HasExited){$newProcess.Kill()}}
        $failed=Join-Path $parent ('.resumestudio-failed-'+[guid]::NewGuid().ToString('N')); Check-OwnedSibling $failed
        Move-Item -LiteralPath $install -Destination $failed
        Move-Item -LiteralPath $backup -Destination $install
        if($request.restart){$null=Start-App $false}
    }
    @{status='failed';error=$message;backup=$backup} | ConvertTo-Json | Set-Content -LiteralPath $result -Encoding UTF8
    if($request.restart){Add-Type -AssemblyName PresentationFramework; [Windows.MessageBox]::Show('更新未完成，已保留或恢复原程序。'+"`n"+$message,'拾简更新') | Out-Null}
    exit 1
}
