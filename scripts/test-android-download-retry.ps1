$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$jdk=if($env:JAVA_HOME){$env:JAVA_HOME}else{'C:\Program Files\Java\jdk-21'}
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('AndroidDownloadRetry-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $fixture 'classes')|Out-Null
try{
    & "$jdk\bin\javac.exe" --release 17 -encoding UTF-8 -d "$fixture\classes" "$root\client-android\src\com\resumestudio\MobileCompat.java" "$root\client-android\src\com\resumestudio\MobileHttp.java" "$root\tests\android\MobileHttpRetryTests.java"
    if($LASTEXITCODE){throw 'Android download retry test compilation failed'}
    & "$jdk\bin\java.exe" -cp "$fixture\classes" com.resumestudio.MobileHttpRetryTests
    if($LASTEXITCODE){throw 'Android download retry test failed'}
}finally{Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
