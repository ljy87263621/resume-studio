param([string]$ApplicationPath='', [string]$OutputDirectory='', [string]$DocumentPath='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/interviews-'+[guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$dotnet=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet).Source}
$fixtures=Join-Path $OutputDirectory 'fixtures'
$testArgs=@($fixtures,(Join-Path $root 'client-winui/Assets/Fonts/MiSans-Regular.ttf'));if($DocumentPath){$testArgs+=[IO.Path]::GetFullPath($DocumentPath)}
& $dotnet run --project (Join-Path $root 'tests/InterviewStudy.Tests/InterviewStudy.Tests.csproj') -c Release -- @testArgs
if($LASTEXITCODE -ne 0){throw 'Interview import and progress checks failed'}
$jdk=$env:JAVA_HOME
if(!$jdk){$jdk='C:\Program Files\Java\jdk-21'}
$classes=Join-Path $root 'backend/build/classes'
$tests=Join-Path $root 'backend/build/test-classes'
New-Item -ItemType Directory -Path $tests -Force|Out-Null
& (Join-Path $jdk 'bin/javac.exe') -encoding UTF-8 -Xlint:all -Werror --release 21 -cp $classes -d $tests (Join-Path $root 'backend/src/test/java/com/resumestudio/InterviewStudyTest.java')
if($LASTEXITCODE -ne 0){throw 'Interview persistence test did not compile'}
& (Join-Path $jdk 'bin/java.exe') --add-modules jdk.httpserver -cp "$classes;$tests" com.resumestudio.InterviewStudyTest
if($LASTEXITCODE -ne 0){throw 'Interview persistence checks failed'}
if($ApplicationPath){
    $savedData=$env:RESUMESTUDIO_TEST_DATA;$savedOutput=$env:RESUMESTUDIO_TEST_OUTPUT;$savedFixtures=$env:RESUMESTUDIO_INTERVIEW_FIXTURES;$savedDocument=$env:RESUMESTUDIO_INTERVIEW_DOCUMENT
    try {
        $env:RESUMESTUDIO_TEST_DATA=Join-Path $OutputDirectory 'isolated-data'
        $env:RESUMESTUDIO_TEST_OUTPUT=$OutputDirectory;$env:RESUMESTUDIO_INTERVIEW_FIXTURES=$fixtures
        $env:RESUMESTUDIO_INTERVIEW_DOCUMENT=if($DocumentPath){[IO.Path]::GetFullPath($DocumentPath)}else{''}
        $process=Start-Process -FilePath ([IO.Path]::GetFullPath($ApplicationPath)) -ArgumentList '--interview-test' -WindowStyle Hidden -PassThru
        if(!$process.WaitForExit(180000)){Stop-Process -Id $process.Id -Force;throw 'Native interview test timed out'}
        $result=Get-Content -LiteralPath (Join-Path $OutputDirectory 'interview-result.json') -Raw|ConvertFrom-Json
        if($result.status -ne 'passed'){Write-Output ($result|ConvertTo-Json -Depth 5);throw $result.error}
        Write-Output "PASS: $($result.checks.Count) native compact favorite and interview study checks"
    } finally {$env:RESUMESTUDIO_TEST_DATA=$savedData;$env:RESUMESTUDIO_TEST_OUTPUT=$savedOutput;$env:RESUMESTUDIO_INTERVIEW_FIXTURES=$savedFixtures;$env:RESUMESTUDIO_INTERVIEW_DOCUMENT=$savedDocument}
}
Write-Output "Evidence: $OutputDirectory"
