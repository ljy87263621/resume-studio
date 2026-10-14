param([switch]$ReuseBackendTestClasses, [switch]$NoRestore)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet).Source}
foreach($suite in @('Navigation.Tests','PlatformCapture.Tests','Sync.Tests')){
    $arguments=@('run','--project',(Join-Path $root "tests/$suite/$suite.csproj"),'-c','Release')
    if($NoRestore){$arguments+='--no-restore'}
    & $dotnet @arguments
    if($LASTEXITCODE -ne 0){throw "Feature verification failed: $suite"}
}
$node=(Get-Command node -ErrorAction Stop).Source
& $node (Join-Path $root 'tests/PlatformCapture.Tests/CaptureScript.Tests.js')
if($LASTEXITCODE -ne 0){throw 'Browser extraction JavaScript fixture verification failed'}
$jdk=$env:JAVA_HOME
if(!$jdk){$jdk='C:\Program Files\Java\jdk-21'}
$backendClasses=Join-Path $root 'backend/build/classes'
$testClasses=Join-Path $root 'backend/build/test-classes'
if(!(Test-Path -LiteralPath (Join-Path $backendClasses 'com/resumestudio/SyncStore.class'))){
    throw 'Compile the current backend first (scripts/build.ps1); SyncStore.class is missing.'
}
if(!$ReuseBackendTestClasses){
    New-Item -ItemType Directory -Force -Path $testClasses | Out-Null
    & (Join-Path $jdk 'bin/javac.exe') -encoding UTF-8 -Xlint:all -Werror --release 21 -cp $backendClasses -d $testClasses (Join-Path $root 'backend/src/test/java/com/resumestudio/SyncStoreTest.java')
    if($LASTEXITCODE -ne 0){throw 'SyncStoreTest did not compile'}
}
if(!(Test-Path -LiteralPath (Join-Path $testClasses 'com/resumestudio/SyncStoreTest.class'))){
    throw 'SyncStoreTest.class is missing; run without -ReuseBackendTestClasses or run scripts/test-offline.ps1 first.'
}
& (Join-Path $jdk 'bin/java.exe') --add-modules jdk.httpserver -cp "$backendClasses;$testClasses" com.resumestudio.SyncStoreTest
if($LASTEXITCODE -ne 0){throw 'SyncStore validation, replacement and recovery verification failed'}
