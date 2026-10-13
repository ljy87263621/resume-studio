param([Parameter(Mandatory)][ValidateSet('stable','nightly')][string]$Channel)
$ErrorActionPreference='Stop'
$repository=$env:GITHUB_REPOSITORY
if($repository -ne 'scj040921/resume-studio'){throw 'Channel publication only supports the official repository'}
$index=Join-Path 'release' ($Channel+'.json')
$bytes=[IO.File]::ReadAllBytes((Join-Path (Get-Location) 'release/release-info.json'))
$signature=(Get-Content release/release-info.sig -Raw).Trim()
$rsa=[Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem((Get-Content client-winui/Assets/UpdatePublicKey.pem -Raw))
    if(!$rsa.VerifyData($bytes,[Convert]::FromBase64String($signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Channel metadata signature is invalid'}
} finally {$rsa.Dispose()}
$info=Get-Content release/release-info.json -Raw|ConvertFrom-Json
if($info.channel -ne $Channel -or $info.packageSize -le 0 -or $info.packageSize -gt 512000000 -or $info.commit -notmatch '^[0-9a-f]{40}$'){throw 'Channel does not match the signed release'}
if(($Channel -eq 'stable' -and $info.tag -ne ('v'+$info.version)) -or ($Channel -eq 'nightly' -and $info.tag -notmatch '^nightly-\d{8}-[0-9a-f]{8}$')){throw 'Invalid channel release tag'}
[IO.File]::WriteAllText($index,(@{data=[Convert]::ToBase64String($bytes);signature=$signature}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
# Do not advertise a release until GitHub confirms its package and metadata are uploaded.
$published=gh release view $info.tag --repo $repository --json assets,tagName,isPrerelease|ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or $published.tagName -ne $info.tag -or $published.isPrerelease -ne ($Channel -eq 'nightly')){throw 'Release is not ready for this channel'}
foreach($name in @($info.packageName,'release-info.json','release-info.sig')){
    if(!@($published.assets|Where-Object {$_.name -eq $name}).Count){throw "Published release is missing $name"}
}
$package=@($published.assets|Where-Object {$_.name -eq $info.packageName})[0]
if($package.size -ne $info.packageSize){throw 'Published package size does not match the signed index'}
gh release view update-channels --repo $repository --json tagName 1>$null 2>$null
if($LASTEXITCODE -ne 0){
    $notes='Signed update-channel indexes for Resume Studio. Program packages remain on their individual Stable and Nightly releases.'
    [IO.File]::WriteAllText('release/channel-notes.txt',$notes,[Text.UTF8Encoding]::new($false))
    gh release create update-channels --repo $repository --target $env:GITHUB_SHA --title '拾简更新通道索引' --prerelease --latest=false --notes-file release/channel-notes.txt
    if($LASTEXITCODE -ne 0){gh release view update-channels --repo $repository --json tagName 1>$null;if($LASTEXITCODE -ne 0){throw 'Cannot create channel index release'}}
}
# A single envelope keeps signed data and its signature together during replacement.
gh release upload update-channels $index --clobber --repo $repository
if($LASTEXITCODE -ne 0){throw 'Signed channel upload failed'}
Write-Output "$Channel channel now points to $($info.tag)."
