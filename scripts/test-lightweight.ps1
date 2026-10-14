param([string]$ReleaseDirectory='release/ResumeStudio')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$app=[IO.Path]::GetFullPath($ReleaseDirectory)
foreach($name in @('ResumeStudio.exe','ResumeStudio.dll','App.xbf','ResumeStudio.pri','Microsoft.ui.xaml.dll','runtime/bin/java.exe','runtime/legal/java.base/LICENSE','licenses','Assets/Fonts/MiSans-Regular.ttf','Assets/Fonts/MiSans-License.pdf')) {
    if(!(Test-Path -LiteralPath (Join-Path $app $name))){throw "Lightweight distribution is missing $name"}
}
$config=Get-Content -LiteralPath (Join-Path $app 'ResumeStudio.runtimeconfig.json') -Raw|ConvertFrom-Json
$identity=Get-Content -LiteralPath (Join-Path $app 'Assets/ReleaseIdentity.json') -Raw|ConvertFrom-Json
if($identity.version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release identity version'}
if($config.runtimeOptions.configProperties.'System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault' -ne $true){throw 'JSON reflection is not preserved'}
if($config.runtimeOptions.configProperties.'System.Runtime.InteropServices.BuiltInComInterop.IsSupported' -ne $true){throw 'Native COM support is not preserved'}
if(!$config.runtimeOptions.includedFrameworks){throw 'Runtime is not self-contained'}
$font=Join-Path $app 'Assets/Fonts/MiSans-Regular.ttf'
if((Get-FileHash -LiteralPath $font).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'client-winui/Assets/Fonts/MiSans-Regular.ttf')).Hash){throw 'Original font changed'}
foreach($image in Get-ChildItem -LiteralPath (Join-Path $root 'client-winui/Assets/Templates') -File -Filter '*.png') {
    if((Get-FileHash -LiteralPath (Join-Path $app ('Assets/Templates/'+$image.Name))).Hash -ne (Get-FileHash -LiteralPath $image.FullName).Hash){throw 'Template preview changed'}
}
$bytes=(Get-ChildItem -LiteralPath $app -Recurse -File|Measure-Object Length -Sum).Sum
if($bytes -gt 150MB){throw "Distribution size regression: $bytes bytes exceeds 150 MiB"}
if((Get-Item -LiteralPath (Join-Path $app 'Microsoft.Windows.SDK.NET.dll')).Length -gt 4MB){throw 'Windows projection was not optimized'}
if((Get-Item -LiteralPath (Join-Path $app 'System.Private.CoreLib.dll')).Length -gt 5MB){throw 'Framework was not optimized'}
Write-Output "PASS: lightweight release budgets, XAML resources, self-contained runtime, original fonts/previews and reflection/COM; $bytes bytes"
