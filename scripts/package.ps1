param([string]$Version = '', [string]$SourceDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if(!$Version) { [xml]$project=Get-Content (Join-Path $root 'client-winui\ResumeStudio.csproj'); $Version=[string]$project.Project.PropertyGroup.Version }
if($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
$source = if ($SourceDirectory) { [IO.Path]::GetFullPath($SourceDirectory) } else { Join-Path $root 'release\ResumeStudio' }
foreach ($required in @('ResumeStudio.exe','ResumeStudio.dll','backend\resume-backend.jar','runtime\bin\java.exe','LICENSE','THIRD_PARTY_NOTICES.md','RUNTIME_NOTICES.md')) {
    if (!(Test-Path (Join-Path $source $required))) { throw "Missing release file: $required" }
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
