param([string]$Version = '', [string]$SourceDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if(!$Version) { [xml]$project=Get-Content (Join-Path $root 'client-winui\ResumeStudio.csproj'); $Version=[string]$project.Project.PropertyGroup.Version }
if($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
$source = if ($SourceDirectory) { [IO.Path]::GetFullPath($SourceDirectory) } else { Join-Path $root 'release\ResumeStudio' }
foreach ($required in @('ResumeStudio.exe','ResumeStudio.dll','Assets\AppIcon.ico','Assets\AppLogo.png','backend\resume-backend.jar','runtime\bin\java.exe','LICENSE','THIRD_PARTY_NOTICES.md','RUNTIME_NOTICES.md')) {
    if (!(Test-Path (Join-Path $source $required))) { throw "Missing release file: $required" }
}
$manifestPath=Join-Path $source 'distribution-manifest.json'
if(!(Test-Path -LiteralPath $manifestPath)){throw 'Distribution manifest is missing; rebuild into a fresh release directory'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$owned=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$owned.Add('distribution-manifest.json')
foreach($file in $manifest.files){
    $path=[IO.Path]::GetFullPath((Join-Path $source $file.path))
    if(!$path.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid distribution manifest path'}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw "Distribution changed: $($file.path)"}
    [void]$owned.Add($file.path)
}
foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){
    if(!$owned.Contains([IO.Path]::GetRelativePath($source,$file.FullName))){throw "Unowned file would be packaged: $($file.Name)"}
}
if (!(Test-Path (Join-Path $source 'runtime\legal\java.base\LICENSE'))) { throw 'Missing OpenJDK license' }
# Personal documents and test logs are never part of a distributable app directory.
$unexpected = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Name -match '\.(bak|log|pfx|key)$' -or $_.Name -eq 'result.json' })
if ($unexpected.Count) { throw ('Unexpected release data: ' + ($unexpected.FullName -join ', ')) }
$zip = Join-Path $root "release\ResumeStudio-v$Version-windows-x64.zip"
if (Test-Path $zip) { throw "Package already exists: $zip" }
Compress-Archive -LiteralPath $source -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $root 'release\SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"
