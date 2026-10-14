param([string]$ApplicationPath='', [string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $root 'release/ResumeStudio/ResumeStudio.exe'}
if(!(Test-Path -LiteralPath $ApplicationPath)){throw "Portable application missing: $ApplicationPath"}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/editor-navigation-'+[guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$previousData=$env:RESUMESTUDIO_TEST_DATA
$previousOutput=$env:RESUMESTUDIO_TEST_OUTPUT
try {
    $env:RESUMESTUDIO_TEST_DATA=Join-Path $OutputDirectory 'isolated-data'
    $env:RESUMESTUDIO_TEST_OUTPUT=$OutputDirectory
    $process=Start-Process -FilePath $ApplicationPath -ArgumentList '--favorites-editor-test' -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(600000)){
        Stop-Process -Id $process.Id -Force
        throw 'Editor navigation verification timed out after 10 minutes'
    }
    $resultPath=Join-Path $OutputDirectory 'result.json'
    if(!(Test-Path -LiteralPath $resultPath)){throw "Native verification produced no result: $resultPath"}
    $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if($result.status -ne 'passed'){throw $result.error}
    Write-Output "PASS: $($result.checks.Count) native editor and navigation checks"
    Write-Output "Evidence: $OutputDirectory"
}
finally {
    $env:RESUMESTUDIO_TEST_DATA=$previousData
    $env:RESUMESTUDIO_TEST_OUTPUT=$previousOutput
}
