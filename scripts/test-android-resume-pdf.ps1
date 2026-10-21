param([string]$Device='emulator-5554',[string]$Apk='release/ResumeStudio-Android-pdf-import.apk')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$build=Join-Path $root 'artifacts/android-resume-pdf-test';New-Item -ItemType Directory -Path "$build/classes","$build/dex" -Force|Out-Null
$sdk=Join-Path $env:LOCALAPPDATA 'Android/Sdk';$tools=Join-Path $sdk 'build-tools/36.0.0';$platform=Join-Path $sdk 'platforms/android-35/android.jar';$jdk=$env:JAVA_HOME
if(!$jdk){$jdk='C:/Program Files/Java/jdk-21'}
adb -s $Device install -r $Apk;if($LASTEXITCODE){throw 'Could not install PDF test build'}
& "$jdk/bin/javac.exe" -encoding UTF-8 --release 17 -classpath $platform -d "$build/classes" "$root/tests/android/ResumePdfInstrumentation.java";if($LASTEXITCODE){throw 'Instrumentation compilation failed'}
& "$jdk/bin/jar.exe" --create --file "$build/classes.jar" -C "$build/classes" .
& "$jdk/bin/java.exe" -cp "$tools/lib/d8.jar" com.android.tools.r8.D8 --min-api 26 --lib $platform --output "$build/dex" "$build/classes.jar";if($LASTEXITCODE){throw 'Instrumentation DEX failed'}
[IO.File]::WriteAllText("$build/AndroidManifest.xml",'<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.resumestudio.pdf.tests"><uses-sdk android:minSdkVersion="26" android:targetSdkVersion="35"/><instrumentation android:name="com.resumestudio.ResumePdfInstrumentation" android:targetPackage="com.resumestudio.mobile"/><application android:label="PDF import verification"/></manifest>')
& "$tools/aapt2.exe" link -o "$build/unsigned.apk" -I $platform --manifest "$build/AndroidManifest.xml" -A "$root/artifacts/resume-pdf-fixtures"
$zip=[IO.Compression.ZipFile]::Open("$build/unsigned.apk",[IO.Compression.ZipArchiveMode]::Update);try{foreach($dex in Get-ChildItem "$build/dex" -Filter '*.dex'){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$dex.FullName,$dex.Name)|Out-Null}}finally{$zip.Dispose()}
& "$tools/zipalign.exe" -f 4 "$build/unsigned.apk" "$build/aligned.apk"
& "$jdk/bin/java.exe" -jar "$tools/lib/apksigner.jar" sign --ks "$env:USERPROFILE/.android/resume-studio-local.p12" --ks-key-alias resume-studio --ks-pass pass:android --out "$build/test.apk" "$build/aligned.apk";if($LASTEXITCODE){throw 'Instrumentation signing failed'}
adb -s $Device install -r "$build/test.apk";if($LASTEXITCODE){throw 'Test APK install failed'}
adb -s $Device shell am instrument -w com.resumestudio.pdf.tests/com.resumestudio.ResumePdfInstrumentation|Tee-Object -FilePath "$build/result.txt"
if((Get-Content "$build/result.txt" -Raw) -notmatch 'INSTRUMENTATION_CODE: 0' -or (Get-Content "$build/result.txt" -Raw) -match 'error=|Process crashed'){throw 'Android PDF import verification failed'}
