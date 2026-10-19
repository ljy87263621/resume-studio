param([ValidateSet('stable','nightly')][string]$Channel='nightly',[Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit)
$ErrorActionPreference='Stop'
$repository='ljy87263621/resume-studio'
if($env:GITHUB_REPOSITORY -ne $repository -or !$env:RESUMESTUDIO_UPDATE_SIGNING_KEY){throw 'Fork repository and signing key required'}
$releases=gh api "repos/$repository/releases?per_page=100"|ConvertFrom-Json
if($LASTEXITCODE){throw 'Cannot query migration releases'}
$work=Join-Path (Get-Location) 'release/platform-migration';New-Item -ItemType Directory -Force $work|Out-Null
$records=@{}
foreach($platform in @('desktop','android')){
    $pattern=if($platform -eq 'android'){if($Channel -eq 'nightly'){'^android-nightly-'}else{'^android-v\d'}}else{if($Channel -eq 'nightly'){'^nightly-'}else{'^v\d'}}
    $candidate=@($releases|Where-Object {!$_.draft -and $_.prerelease -eq ($Channel -eq 'nightly') -and $_.tag_name -match $pattern -and ($_.target_commitish -eq $Commit -or $_.body.Contains("source-commit: $Commit"))}|Sort-Object published_at -Descending|Select-Object -First 1)
    if(!$candidate.Count){throw "Publish $platform $Channel for this commit before migration"}
    $prefix=if($platform -eq 'android'){'android-release-info'}else{'release-info'}
    $dir=Join-Path $work $platform;New-Item -ItemType Directory -Force $dir|Out-Null
    gh release download $candidate[0].tag_name --repo $repository -p "$prefix.json" -p "$prefix.sig" -D $dir --clobber
    if($LASTEXITCODE){throw 'Cannot download migration metadata'}
    $bytes=[IO.File]::ReadAllBytes((Join-Path $dir "$prefix.json"));$rsa=[Security.Cryptography.RSA]::Create()
    try{
        $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../client-winui/Assets/UpdatePublicKey.pem')))
        if(!$rsa.VerifyData($bytes,[Convert]::FromBase64String((Get-Content (Join-Path $dir "$prefix.sig") -Raw).Trim()),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Migration signature is invalid'}
    }finally{$rsa.Dispose()}
    $info=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -AsHashtable
    if($info.platform -ne $platform -or $info.commit -ne $Commit -or $info.channel -ne $Channel -or $info.tag -ne $candidate[0].tag_name){throw 'Migration source identity mismatch'}
    $records[$platform]=$info
}
$desktop=$records.desktop;$android=$records.android
if($desktop.version -ne $android.version){throw 'One-time legacy migration requires matching base versions'}
$suffix=if($Channel -eq 'stable'){''}else{'-nightly.'+$desktop.tag.Substring(8).Replace('-','.')}
if($android.androidVersionName -ne ($desktop.version+$suffix)){throw 'Legacy migration date/version must match both releases'}
$apk=Join-Path $work $android.androidPackageName
gh release download $android.tag --repo $repository -p $android.androidPackageName -D $work --clobber
if($LASTEXITCODE -or (Get-Item $apk).Length -ne $android.androidPackageSize -or (Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant() -ne $android.androidSha256){throw 'Migration APK failed signed hash verification'}
foreach($key in $android.Keys){if($key.StartsWith('android')){$desktop[$key]=$android[$key]}}
$bytes=[Text.Encoding]::UTF8.GetBytes(($desktop|ConvertTo-Json -Depth 8));$rsa=[Security.Cryptography.RSA]::Create()
try{$rsa.ImportFromPem($env:RESUMESTUDIO_UPDATE_SIGNING_KEY);$signature=[Convert]::ToBase64String($rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss))}finally{$rsa.Dispose()}
$metadata=Join-Path $work 'release-info.json';$sig=Join-Path $work 'release-info.sig';$index=Join-Path $work "$Channel.json"
[IO.File]::WriteAllBytes($metadata,$bytes);[IO.File]::WriteAllText($sig,$signature)
[IO.File]::WriteAllText($index,(@{data=[Convert]::ToBase64String($bytes);signature=$signature}|ConvertTo-Json -Compress))
# Only the compatibility release carries both packages. Platform indexes are never changed here.
gh release upload $desktop.tag $apk --clobber --repo $repository
if($LASTEXITCODE){throw 'Migration release upload failed'}
gh release upload update-channels $index --clobber --repo $repository
if($LASTEXITCODE){throw 'Legacy migration index upload failed'}
