param([string]$Sdk=$env:ANDROID_HOME)
$ErrorActionPreference='Stop'
if(!$Sdk){$Sdk=$env:ANDROID_SDK_ROOT}
if(!$Sdk){throw 'Android SDK is not installed on this runner'}
$manager=Get-ChildItem -LiteralPath (Join-Path $Sdk 'cmdline-tools') -Filter sdkmanager.bat -Recurse -ErrorAction SilentlyContinue|Select-Object -First 1
if(!$manager){throw 'Android command-line SDK manager is missing'}
$arguments=@('--sdk_root='+$Sdk,'platforms;android-35','build-tools;36.0.0')
$accepted=1..10|ForEach-Object {'y'}
$accepted|& $manager.FullName @arguments
if($LASTEXITCODE -ne 0){throw 'Android SDK packages could not be installed'}
foreach($required in @((Join-Path $Sdk 'platforms\android-35\android.jar'),(Join-Path $Sdk 'build-tools\36.0.0\aapt2.exe'))){if(!(Test-Path -LiteralPath $required)){throw "Android SDK package missing: $required"}}
