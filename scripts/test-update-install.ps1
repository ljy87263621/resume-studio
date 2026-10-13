$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('resume-install-test-'+[guid]::NewGuid().ToString('N'))
$install=Join-Path $root 'app'; $data=Join-Path $root 'data'; $workspace=Join-Path $data ('updates\'+[guid]::NewGuid().ToString('N')); $source=Join-Path $workspace 'extracted\ResumeStudio'
New-Item -ItemType Directory -Force $install,$source | Out-Null
foreach($name in @('Update-App.ps1','sync-distribution.ps1')){[IO.File]::WriteAllText((Join-Path $workspace $name),[IO.File]::ReadAllText((Join-Path $PSScriptRoot $name)),[Text.UTF8Encoding]::new($true))}
function Manifest([string]$dir,[string[]]$names){
    $files=@($names | ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $dir $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})
    @{files=$files} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dir 'distribution-manifest.json') -Encoding UTF8
}
Copy-Item -LiteralPath (Join-Path $env:WINDIR 'System32\cmd.exe') -Destination (Join-Path $install 'ResumeStudio.exe')
'obsolete' | Set-Content (Join-Path $install 'obsolete.dll'); 'original' | Set-Content (Join-Path $install 'modified.dll')
Manifest $install @('ResumeStudio.exe','obsolete.dll','modified.dll')
'user-modified' | Set-Content (Join-Path $install 'modified.dll'); 'keep unknown' | Set-Content (Join-Path $install 'personal.txt'); 'data sentinel' | Set-Content (Join-Path $data 'sentinel.txt')
Copy-Item -LiteralPath (Join-Path $install 'ResumeStudio.exe') -Destination $source
'new program' | Set-Content (Join-Path $source 'new.dll'); Manifest $source @('ResumeStudio.exe','new.dll')
$request=[ordered]@{processId=99999999;installRoot=$install;sourceRoot=$source;workspace=$workspace;dataDirectory=$data;restart=$false}
$requestFile=Join-Path $workspace 'request.json'; $request | ConvertTo-Json | Set-Content $requestFile -Encoding UTF8
# Reproduce the installed shortcut: the child updater inherits the application directory.
Push-Location -LiteralPath $install
try { & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspace 'Update-App.ps1') -RequestFile $requestFile }
finally { Pop-Location }
if($LASTEXITCODE -ne 0){throw 'Transactional install failed'}
if(!(Test-Path (Join-Path $install 'personal.txt')) -or !(Test-Path (Join-Path $install 'modified.dll')) -or (Test-Path (Join-Path $install 'obsolete.dll')) -or !(Test-Path (Join-Path $install 'new.dll')) -or (Get-Content (Join-Path $data 'sentinel.txt')) -ne 'data sentinel'){throw 'Install failed to preserve data or modified files'}
Write-Output 'PASS: transactional install, obsolete removal, modified and unknown files, personal data preservation'
Write-Output 'PASS: inherited application working directory does not block the directory swap'
# A publisher can accidentally ship an invalid executable; startup failure must restore the former directory.
$request.restart=$true; 'invalid executable' | Set-Content (Join-Path $source 'ResumeStudio.exe'); Manifest $source @('ResumeStudio.exe','new.dll')
$request | ConvertTo-Json | Set-Content $requestFile -Encoding UTF8
# Avoid a modal error box in the isolated fixture; production retains its user-facing failure message.
$helper=Get-Content (Join-Path $workspace 'Update-App.ps1') -Raw
$helper=$helper.Replace("if(`$request.restart){Add-Type -AssemblyName PresentationFramework;","if(`$false){Add-Type -AssemblyName PresentationFramework;")
[IO.File]::WriteAllText((Join-Path $workspace 'Update-App.ps1'),$helper,[Text.UTF8Encoding]::new($true))
& (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspace 'Update-App.ps1') -RequestFile $requestFile
if($LASTEXITCODE -eq 0){throw 'Invalid executable unexpectedly installed'}
if((Get-FileHash (Join-Path $install 'ResumeStudio.exe')).Hash -ne (Get-FileHash (Join-Path $env:WINDIR 'System32\cmd.exe')).Hash){throw 'Rollback failed'}
@(Get-Process ResumeStudio -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $install 'ResumeStudio.exe')) | Stop-Process
$global:LASTEXITCODE=0
Write-Output 'PASS: failed startup rolls back the application directory'
