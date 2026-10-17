param([string]$ReleaseDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$jdk=$env:JAVA_HOME
if(!$jdk){$jdk='C:\Program Files\Java\jdk-21'}
$tests=@(Get-ChildItem (Join-Path $root 'backend/src/test/java') -Recurse -Filter '*.java' | ForEach-Object FullName)
$classes=Join-Path $root 'backend/build/test-classes'; New-Item -ItemType Directory -Force $classes | Out-Null
& (Join-Path $jdk 'bin/javac.exe') -encoding UTF-8 -Xlint:all -Werror --release 21 -cp (Join-Path $root 'backend/build/classes') -d $classes @tests
if($LASTEXITCODE -ne 0){throw 'Offline tests did not compile'}
foreach($test in @('CompanyJobsTest','JobCaptureTest','WorkflowTest','ScheduleTest','OptimizationTest')){
    & (Join-Path $jdk 'bin/java.exe') --add-modules jdk.httpserver -cp ((Join-Path $root 'backend/build/classes')+';'+$classes) "com.resumestudio.$test"
    if($LASTEXITCODE -ne 0){throw "Offline test failed: $test"}
}
$distribution=if($ReleaseDirectory){[IO.Path]::GetFullPath($ReleaseDirectory)}else{Join-Path $root 'release/ResumeStudio'}
& (Join-Path $jdk 'bin/java.exe') -cp ((Join-Path $root 'backend/build/classes')+';'+$classes) com.resumestudio.TemplateCatalogTest (Join-Path $distribution 'Assets/TemplateCatalog.json')
if($LASTEXITCODE -ne 0){throw 'Packaged template catalogue encoding or content check failed'}
