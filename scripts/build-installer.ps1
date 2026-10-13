param([string]$SourceDirectory='',[string]$PackageLabel='',[string]$Compiler='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=if($SourceDirectory){[IO.Path]::GetFullPath($SourceDirectory)}else{Join-Path $root 'release/ResumeStudio'}
$identity=Get-Content (Join-Path $source 'Assets/ReleaseIdentity.json') -Raw|ConvertFrom-Json
if($identity.version -notmatch '^\d+\.\d+\.\d+$' -or $identity.channel -notin @('stable','nightly')){throw 'Invalid source build identity'}
$expectedLabel=$identity.version
if($identity.channel -eq 'nightly'){
    if($identity.tag -notmatch '^nightly-(\d{8})-([0-9a-f]{8})$'){throw 'Invalid Nightly source tag'}
    $expectedLabel=$identity.version+'-nightly.'+$Matches[1]+'.'+$Matches[2]
}
if(!$PackageLabel){$PackageLabel=$expectedLabel}
if($PackageLabel -notmatch '^\d+\.\d+\.\d+(?:-nightly\.\d{8}\.[0-9a-f]{8})?$'){throw 'Invalid installer package label'}
if($PackageLabel -ne $expectedLabel){throw 'Installer label does not match its build identity'}
if(!$Compiler){$Compiler=$env:RESUMESTUDIO_ISCC}
if(!$Compiler){$Compiler=Join-Path $root 'artifacts/installer-tools/InnoSetup-6.7.3/ISCC.exe'}
$bootstrapper=$env:RESUMESTUDIO_WEBVIEW_BOOTSTRAPPER
if(!$bootstrapper){$bootstrapper=Join-Path $root 'artifacts/installer-tools/MicrosoftEdgeWebview2Setup.exe'}
foreach($path in @($Compiler,$bootstrapper,(Join-Path $source 'ResumeStudio.exe'))){if(!(Test-Path -LiteralPath $path)){throw "Required installer input missing: $path"}}
# The exact same manifest-owned payload is used by the portable ZIP and Setup.
$manifest=Get-Content (Join-Path $source 'distribution-manifest.json') -Raw|ConvertFrom-Json
$owned=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$owned.Add('distribution-manifest.json')
foreach($file in $manifest.files){
    $payload=[IO.Path]::GetFullPath((Join-Path $source $file.path))
    if(!$payload.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne $file.sha256){throw 'Installer payload failed manifest validation'}
    [void]$owned.Add($file.path)
}
foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){
    if(!$owned.Contains([IO.Path]::GetRelativePath($source,$file.FullName))){throw "Unowned file would be included in Setup: $($file.Name)"}
}
$output=Join-Path $root 'release'
$scriptPath=Join-Path $root 'artifacts/ResumeStudio-installer.iss'
# Inno's Chinese script must not depend on the runner's console code page.
[IO.File]::WriteAllText($scriptPath,[IO.File]::ReadAllText((Join-Path $root 'installer/ResumeStudio.iss')),[Text.UTF8Encoding]::new($true))
$channelName=if($identity.channel -eq 'nightly'){'Nightly'}else{'Stable'}
& $Compiler "/DSourceDir=$source" "/DAppVersion=$($identity.version)" "/DPackageLabel=$PackageLabel" "/DChannelName=$channelName" "/DOutputDir=$output" "/DBootstrapper=$bootstrapper" "/DCompilerLicense=$(Join-Path (Split-Path $Compiler -Parent) 'license.txt')" "/DLanguageFile=$(Join-Path $root 'installer/ChineseSimplified.isl')" $scriptPath
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $output "ResumeStudio-v$PackageLabel-Setup-x64.exe"
if(!(Test-Path -LiteralPath $setup)){throw 'Installer was not produced'}
$hash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Content (Join-Path $output 'SHA256SUMS.txt') "$hash  $([IO.Path]::GetFileName($setup))" -Encoding utf8
Write-Output "Installer: $setup"
