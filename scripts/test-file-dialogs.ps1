param([string]$ApplicationPath='', [Parameter(Mandatory)][string]$ResumeFixture, [string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $root 'release/ResumeStudio/ResumeStudio.exe'}
if(!(Test-Path -LiteralPath $ApplicationPath)){throw "Portable application missing: $ApplicationPath"}
if(!(Test-Path -LiteralPath $ResumeFixture)){throw "Resume fixture missing: $ResumeFixture"}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/file-dialogs-'+[guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory to avoid a native overwrite confirmation'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$savedData=$env:RESUMESTUDIO_TEST_DATA
$savedOutput=$env:RESUMESTUDIO_TEST_OUTPUT
$savedFixture=$env:RESUMESTUDIO_RESUME_FIXTURE
try {
    $env:RESUMESTUDIO_TEST_DATA=Join-Path $OutputDirectory 'isolated-data'
    $env:RESUMESTUDIO_TEST_OUTPUT=$OutputDirectory
    $env:RESUMESTUDIO_RESUME_FIXTURE=[IO.Path]::GetFullPath($ResumeFixture)
    $process=Start-Process -FilePath ([IO.Path]::GetFullPath($ApplicationPath)) -ArgumentList '--picker-test' -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(60000)){
        Stop-Process -Id $process.Id -Force
        throw 'Native file dialog verification timed out after 60 seconds'
    }
    $results=@(Get-Content -LiteralPath (Join-Path $OutputDirectory 'picker-result.json') -Raw | ConvertFrom-Json)
    if($results.Count -ne 4 -or @($results | Where-Object status -ne 'passed').Count){throw ($results | ConvertTo-Json -Depth 8)}
    Write-Output 'PASS: native open/save selection, cancellation, JSON roundtrip, photo resizing/persistence and resume-switch guard'
    Write-Output "Evidence: $OutputDirectory"
} finally {
    $env:RESUMESTUDIO_TEST_DATA=$savedData
    $env:RESUMESTUDIO_TEST_OUTPUT=$savedOutput
    $env:RESUMESTUDIO_RESUME_FIXTURE=$savedFixture
}
