param([Parameter(Mandatory)][string]$SourceDirectory,[Parameter(Mandatory)][string]$DestinationDirectory,[string]$PreviousSourceDirectory='')
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
$destination=[IO.Path]::GetFullPath($DestinationDirectory).TrimEnd('\')
if($destination -eq [IO.Path]::GetPathRoot($destination).TrimEnd('\') -or $destination -eq $source -or $source.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid distribution destination' }
foreach($directory in @($source,$destination)) {
    if((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Distribution directory must not be a reparse point' }
}
function Read-Manifest([string]$directory) {
    $path=Join-Path $directory 'distribution-manifest.json'
    if(Test-Path -LiteralPath $path) { return @((Get-Content -LiteralPath $path -Raw|ConvertFrom-Json).files) }
    return @()
}
function Resolve-Payload([string]$directory,[string]$relative) {
    $resolved=[IO.Path]::GetFullPath((Join-Path $directory $relative))
    if(!$resolved.StartsWith($directory+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path leaves the application directory' }
    # Reparse points must not redirect installation cleanup into another directory.
    $ancestor=$resolved
    while($ancestor -ne $directory) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Application payload contains a reparse point' }
        $ancestor=Split-Path $ancestor -Parent
    }
    return $resolved
}
$current=Read-Manifest $source
if(!$current.Count) { throw 'Source distribution manifest is missing' }
$previous=Read-Manifest $destination
if(!$previous.Count -and $PreviousSourceDirectory) {
    $baseline=[IO.Path]::GetFullPath($PreviousSourceDirectory).TrimEnd('\')
    $previous=@(Get-ChildItem -LiteralPath $baseline -File -Recurse|ForEach-Object {
        [pscustomobject]@{path=[IO.Path]::GetRelativePath($baseline,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
if(!$previous.Count -and (Test-Path (Join-Path $destination 'ResumeStudio.exe'))) { throw 'Legacy installation requires -PreviousSourceDirectory pointing to its original distribution' }
$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($file in $current) {
    $payload=Resolve-Payload $source $file.path
    if((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne $file.sha256) { throw "Source payload changed: $($file.path)" }
    [void]$paths.Add($file.path)
}
$obsolete=@()
foreach($file in $previous) {
    if($paths.Contains($file.path) -or $file.path -eq 'distribution-manifest.json') { continue }
    $path=Resolve-Payload $destination $file.path
    if(Test-Path -LiteralPath $path) {
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $file.sha256) { $obsolete+= $path }
        else { Write-Warning "Preserved modified file: $($file.path)" }
    }
}
New-Item -ItemType Directory -Force $destination|Out-Null
foreach($file in $current) {
    $target=Resolve-Payload $destination $file.path
    New-Item -ItemType Directory -Force (Split-Path $target -Parent)|Out-Null
    if(!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) {
        Copy-Item -LiteralPath (Resolve-Payload $source $file.path) -Destination $target -Force
    }
}
Copy-Item -LiteralPath (Join-Path $source 'distribution-manifest.json') -Destination $destination -Force
foreach($file in $current) {
    if((Get-FileHash -LiteralPath (Resolve-Payload $destination $file.path) -Algorithm SHA256).Hash -ne $file.sha256) { throw "Installed payload mismatch: $($file.path)" }
}
# Delete only obsolete, unmodified files owned by the previous distribution, after the new payload verifies.
foreach($path in $obsolete) { Remove-Item -LiteralPath $path -Force }
Write-Output "Distribution synchronized; removed $($obsolete.Count) obsolete application files."
