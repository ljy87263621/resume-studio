param([Parameter(Mandatory)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$app=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
if($app -eq [IO.Path]::GetPathRoot($app).TrimEnd('\') -or !(Test-Path -LiteralPath (Join-Path $app 'ResumeStudio.exe'))){throw 'Invalid application compression directory'}
if((Get-Item -LiteralPath $app -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Application compression cannot follow a reparse point'}
$manifest=Get-Content -LiteralPath (Join-Path $app 'distribution-manifest.json') -Raw|ConvertFrom-Json
if(!$manifest.files -or $manifest.files.Count -gt 5000){throw 'Invalid application compression manifest'}
$paths=New-Object 'Collections.Generic.List[string]'
function PayloadHash([string]$path){
    $algorithm=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($path)
    try {return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','')}
    finally {$stream.Dispose();$algorithm.Dispose()}
}
foreach($file in $manifest.files){
    $path=[IO.Path]::GetFullPath((Join-Path $app $file.path))
    if(!$path.StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Compression path leaves the application directory'}
    $ancestor=$path
    while($ancestor -ne $app){
        if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Compression cannot follow a reparse point'}
        $ancestor=Split-Path $ancestor -Parent
    }
    if((PayloadHash $path) -eq $file.sha256){$paths.Add($path)}
}
# Restrict compression to unmodified manifest-owned files. It does not change bytes,
# signatures, or user data, and needs no separate Java/.NET installation.
$batch=New-Object 'Collections.Generic.List[string]';$characters=0
foreach($path in $paths){
    if($characters+$path.Length -gt 16000 -and $batch.Count){
        & "$env:WINDIR\System32\compact.exe" /C /EXE:LZX /I /Q @batch
        $batch.Clear();$characters=0
    }
    $batch.Add($path);$characters+=$path.Length+3
}
if($batch.Count){& "$env:WINDIR\System32\compact.exe" /C /EXE:LZX /I /Q @batch}
# Unsupported volumes or files in use must not prevent installation or startup.
exit 0
