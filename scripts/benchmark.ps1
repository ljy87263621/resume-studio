param([string]$ApplicationDirectory, [int]$Samples = 3, [string]$Label = 'current')
$ErrorActionPreference='Stop'
if($Samples -lt 1 -or $Samples -gt 10){throw 'Samples must be between 1 and 10'}
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path ([IO.Path]::GetFullPath($ApplicationDirectory)) 'ResumeStudio.exe'
if(!(Test-Path $exe)){throw 'Application directory missing'}
$results=@()
for($index=1;$index -le $Samples;$index++)
{
    $directory=Join-Path $root ('artifacts\benchmarks\'+$Label+'-'+[guid]::NewGuid().ToString('N'))
    $env:RESUMESTUDIO_TEST_DATA=Join-Path $directory 'data'; $env:RESUMESTUDIO_TEST_OUTPUT=Join-Path $directory 'output'
    $process=Start-Process -FilePath $exe -ArgumentList '--benchmark' -PassThru
    try
    {
        $file=Join-Path $env:RESUMESTUDIO_TEST_OUTPUT 'performance.json'
        $deadline=[DateTime]::UtcNow.AddSeconds(45)
        while(!(Test-Path $file)) { if($process.HasExited -or [DateTime]::UtcNow -gt $deadline){throw 'Benchmark did not complete'}; Start-Sleep -Milliseconds 150 }
        $sample=Get-Content $file -Raw | ConvertFrom-Json
        if($sample.status -ne 'passed'){throw $sample.error}
        $results+=$sample
        Write-Output "Sample $index : first page $($sample.startup.firstPageReady) ms, repeat editor $($sample.repeatEditorMs) ms"
    }
    finally
    {
        if(!$process.HasExited){$null=$process.CloseMainWindow(); if(!$process.WaitForExit(10000)){throw 'Benchmark could not close after saving'}}
    }
}
$resultFile=Join-Path $root ('artifacts\benchmarks\'+$Label+'-results.json')
ConvertTo-Json -InputObject @($results) -Depth 20 | Set-Content $resultFile -Encoding utf8
Write-Output "Results: $resultFile"
