param([Parameter(Mandatory)][ValidateSet('stable','nightly')][string]$Channel)
$ErrorActionPreference='Stop';$repository='ljy87263621/resume-studio'
if($env:GITHUB_REPOSITORY -ne $repository){throw 'Only this Fork repository may publish Android channels'}
$bytes=[IO.File]::ReadAllBytes((Join-Path (Get-Location) 'release/android-release-info.json'))
$signature=(Get-Content release/android-release-info.sig -Raw).Trim();$rsa=[Security.Cryptography.RSA]::Create()
try{
    $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../client-winui/Assets/UpdatePublicKey.pem')))
    if(!$rsa.VerifyData($bytes,[Convert]::FromBase64String($signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Invalid Android channel signature'}
}finally{$rsa.Dispose()}
$info=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
if($info.platform -ne 'android' -or $info.channel -ne $Channel -or $info.version -notmatch '^\d+\.\d+\.\d+$' -or $info.commit -notmatch '^[0-9a-f]{40}$' -or $info.androidSha256 -notmatch '^[0-9a-f]{64}$' -or $info.androidVersionCode -le 0 -or $info.androidPackageSize -le 0 -or $info.androidPackageSize -gt 150000000){throw 'Invalid signed Android identity'}
$suffix=if($Channel -eq 'stable'){if($info.tag -ne "android-v$($info.version)"){throw 'Android Stable tag mismatch'};''}else{
    if($info.tag -notmatch '^android-nightly-\d{8}-[0-9a-f]{8}$' -or !$info.tag.EndsWith($info.commit.Substring(0,8))){throw 'Android Nightly tag mismatch'}
    '-nightly.'+$info.tag.Substring('android-nightly-'.Length).Replace('-','.')
}
if($info.androidVersionName -ne ($info.version+$suffix) -or $info.androidPackageName -ne "ResumeStudio-Android-v$($info.version)$suffix.apk"){throw 'Android package name mismatch'}
$published=gh release view $info.tag --repo $repository --json assets,tagName,isPrerelease|ConvertFrom-Json
if($LASTEXITCODE -or $published.tagName -ne $info.tag -or $published.isPrerelease -ne ($Channel -eq 'nightly')){throw 'Android release is not ready'}
foreach($entry in @(@($info.androidPackageName,$info.androidPackageSize,$info.androidSha256),@('android-release-info.json',$bytes.Length,[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()),@('android-release-info.sig',(Get-Item release/android-release-info.sig).Length,(Get-FileHash release/android-release-info.sig -Algorithm SHA256).Hash.ToLowerInvariant()))){
    $asset=@($published.assets|Where-Object name -eq $entry[0])
    if($asset.Count -ne 1 -or $asset[0].size -ne $entry[1] -or $asset[0].digest -ne ('sha256:'+$entry[2])){throw 'Android asset digest mismatch'}
}
$index="release/android-$Channel.json"
[IO.File]::WriteAllText($index,(@{data=[Convert]::ToBase64String($bytes);signature=$signature}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
gh release upload update-channels $index --clobber --repo $repository
if($LASTEXITCODE){throw 'Android channel publication failed'}
