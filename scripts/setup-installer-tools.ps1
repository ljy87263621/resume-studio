$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tools=Join-Path $root 'artifacts/installer-tools'
New-Item -ItemType Directory -Path $tools -Force|Out-Null
$compilerRoot=Join-Path $tools 'InnoSetup-6.7.3'
$compiler=Join-Path $compilerRoot 'ISCC.exe'
if(!(Test-Path -LiteralPath $compiler)){
    $setup=Join-Path $tools 'innosetup-6.7.3.exe'
    if(!(Test-Path -LiteralPath $setup)){Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $setup}
    if((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'){throw 'Inno Setup compiler download hash mismatch'}
    $signature=Get-AuthenticodeSignature -LiteralPath $setup
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.'){throw 'Inno Setup compiler publisher verification failed'}
    $start=[Diagnostics.ProcessStartInfo]::new($setup)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    foreach($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER','/NOICONS',('/DIR='+$compilerRoot))){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start);$process.WaitForExit()
    if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)){throw 'Inno Setup compiler setup failed'}
}
$bootstrapper=Join-Path $tools 'MicrosoftEdgeWebview2Setup.exe'
if(!(Test-Path -LiteralPath $bootstrapper)){Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper}
$signature=Get-AuthenticodeSignature -LiteralPath $bootstrapper
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'WebView2 bootstrapper publisher verification failed'}
if($env:GITHUB_ENV){Add-Content $env:GITHUB_ENV "RESUMESTUDIO_ISCC=$compiler";Add-Content $env:GITHUB_ENV "RESUMESTUDIO_WEBVIEW_BOOTSTRAPPER=$bootstrapper"}
Write-Output "Verified installer tools: $compiler"
