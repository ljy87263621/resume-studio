param([string]$ApplicationPath='', [string]$OutputDirectory='', [switch]$TextOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/resume-import-'+[guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$fixtures=Join-Path $OutputDirectory 'fixtures';$dotnet=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe';if(!(Test-Path $dotnet)){$dotnet=(Get-Command dotnet).Source}
$extra=@();if($TextOnly){$extra=@('--text-only')}
& $dotnet run --project "$root/tests/ResumePdf.Tests/ResumePdf.Tests.csproj" -c Release -- $fixtures @extra;if($LASTEXITCODE){throw 'Desktop PDF extraction verification failed'}
$jdk=if($env:JAVA_HOME){$env:JAVA_HOME}else{'C:/Program Files/Java/jdk-21'};$classes=Join-Path $root 'backend/build/classes';$tests=Join-Path $root 'backend/build/test-classes';New-Item -ItemType Directory -Path $tests -Force|Out-Null
& "$jdk/bin/javac.exe" -encoding UTF-8 -Xlint:all -Werror --release 21 -cp $classes -d $tests "$root/backend/src/test/java/com/resumestudio/ResumeImportTest.java";if($LASTEXITCODE){throw 'Resume import mapping test compilation failed'}
$mappingArgs=@();if(!$TextOnly){$mappingArgs=@($fixtures)}
& "$jdk/bin/java.exe" --add-modules jdk.httpserver -cp "$classes;$tests" com.resumestudio.ResumeImportTest @mappingArgs;if($LASTEXITCODE){throw 'Resume import mapping verification failed'}
if($ApplicationPath){
    $savedData=$env:RESUMESTUDIO_TEST_DATA;$savedOutput=$env:RESUMESTUDIO_TEST_OUTPUT;$savedFixtures=$env:RESUMESTUDIO_PDF_FIXTURES
    try {
        $env:RESUMESTUDIO_TEST_DATA=Join-Path $OutputDirectory 'isolated-data';$env:RESUMESTUDIO_TEST_OUTPUT=$OutputDirectory;$env:RESUMESTUDIO_PDF_FIXTURES=$fixtures
        $process=Start-Process -FilePath ([IO.Path]::GetFullPath($ApplicationPath)) -ArgumentList '--resume-import-test' -WindowStyle Hidden -PassThru
        if(!$process.WaitForExit(180000)){Stop-Process -Id $process.Id -Force;throw 'Desktop PDF import UI timed out'}
        $result=Get-Content (Join-Path $OutputDirectory 'result.json') -Raw|ConvertFrom-Json;if($result.status -ne 'passed'){throw $result.error};Write-Output "PASS: $($result.checks.Count) native desktop PDF import checks"
    } finally {$env:RESUMESTUDIO_TEST_DATA=$savedData;$env:RESUMESTUDIO_TEST_OUTPUT=$savedOutput;$env:RESUMESTUDIO_PDF_FIXTURES=$savedFixtures}
}
Write-Output "Evidence: $OutputDirectory"
