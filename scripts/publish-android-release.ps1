param(
    [Parameter(Mandatory)][ValidateSet('stable','nightly')][string]$Channel,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$VersionName,
    [Parameter(Mandatory)][int]$VersionCode,
    [Parameter(Mandatory)][string]$PackagePath,
    [switch]$SignOnly
)
$ErrorActionPreference='Stop'
$repository='scj040921/resume-studio'
if($Version -notmatch '^\d+\.\d+\.\d+$' -or $Commit -notmatch '^[0-9a-f]{40}$' -or $VersionCode -le 0){throw 'Invalid Android release identity'}
$suffix=if($Channel -eq 'nightly'){
    if($Tag -notmatch '^android-nightly-\d{8}-[0-9a-f]{8}$' -or !$Tag.EndsWith($Commit.Substring(0,8))){throw 'Invalid Android Nightly tag'}
    '-nightly.'+$Tag.Substring('android-nightly-'.Length).Replace('-','.')
}else{
    if($Tag -ne "android-v$Version"){throw 'Invalid Android Stable tag'}
    ''
}
if($VersionName -ne ($Version+$suffix) -or [IO.Path]::GetFileName($PackagePath) -ne "ResumeStudio-Android-v$Version$suffix.apk"){throw 'Android package identity mismatch'}
$size=(Get-Item -LiteralPath $PackagePath).Length
if($size -le 0 -or $size -gt 150000000){throw 'Android APK size is invalid'}
if(!$env:RESUMESTUDIO_UPDATE_SIGNING_KEY){throw 'Update signing key is not configured'}
$info=[ordered]@{platform='android';channel=$Channel;version=$Version;commit=$Commit;tag=$Tag;builtAt=[DateTimeOffset]::UtcNow.ToString('o');androidPackageName=[IO.Path]::GetFileName($PackagePath);androidSha256=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant();androidPackageSize=$size;androidVersionName=$VersionName;androidVersionCode=$VersionCode}
$bytes=[Text.Encoding]::UTF8.GetBytes(($info|ConvertTo-Json))
$rsa=[Security.Cryptography.RSA]::Create()
try{
    $rsa.ImportFromPem($env:RESUMESTUDIO_UPDATE_SIGNING_KEY)
    $signature=[Convert]::ToBase64String($rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss))
}finally{$rsa.Dispose()}
New-Item -ItemType Directory -Force release|Out-Null
[IO.File]::WriteAllBytes('release/android-release-info.json',$bytes)
[IO.File]::WriteAllText('release/android-release-info.sig',$signature,[Text.UTF8Encoding]::new($false))
$index="release/android-$Channel.json"
[IO.File]::WriteAllText($index,(@{data=[Convert]::ToBase64String($bytes);signature=$signature}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
if($SignOnly){return}
if($env:GITHUB_REPOSITORY -ne $repository){throw 'Android publication only supports the official repository'}
if($Channel -eq 'stable'){& "$PSScriptRoot/verify-android-nightly.ps1" -Commit $Commit -Version $Version}
$notes="Android $VersionName`n`n<!-- source-commit: $Commit -->`n`n仅更新安卓端，桌面更新通道保持独立。"
[IO.File]::WriteAllText('release/android-notes.md',$notes,[Text.UTF8Encoding]::new($false))
$releaseArgs=@('release','create',$Tag,$PackagePath,'release/android-release-info.json','release/android-release-info.sig','--repo',$repository,'--target',$Commit,'--title',"拾简 Android $VersionName",'--latest=false','--notes-file','release/android-notes.md')
if($Channel -eq 'nightly'){$releaseArgs+='--prerelease'}
& gh @releaseArgs
if($LASTEXITCODE){throw 'Android release publication failed'}
& "$PSScriptRoot/publish-android-channel.ps1" -Channel $Channel
