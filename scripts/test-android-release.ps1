$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('AndroidRelease-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$fixture/release","$fixture/scripts","$fixture/client-winui/Assets"|Out-Null
Copy-Item "$PSScriptRoot/publish-android-channel.ps1" "$fixture/scripts/"
$rsa=[Security.Cryptography.RSA]::Create(2048);$savedKey=$env:RESUMESTUDIO_UPDATE_SIGNING_KEY
$savedRepo=$env:GITHUB_REPOSITORY
[IO.File]::WriteAllText("$fixture/client-winui/Assets/UpdatePublicKey.pem",$rsa.ExportSubjectPublicKeyInfoPem())
$global:androidChannelUploads=0;$global:androidChannelBadDigest=$false
function gh {
    $global:LASTEXITCODE=0
    if($args[0] -eq 'release' -and $args[1] -eq 'view'){
        $info=Get-Content release/android-release-info.json -Raw|ConvertFrom-Json
        $assets=@(@{name=$info.androidPackageName;size=$info.androidPackageSize;digest=('sha256:'+$info.androidSha256)})
        foreach($file in @('release/android-release-info.json','release/android-release-info.sig')){$assets+=@{name=(Split-Path $file -Leaf);size=(Get-Item $file).Length;digest=('sha256:'+(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())}}
        if($global:androidChannelBadDigest){$assets[0].digest='sha256:invalid'}
        @{tagName=$info.tag;isPrerelease=($info.channel -eq 'nightly');assets=$assets}|ConvertTo-Json -Depth 6;return
    }
    if($args[0] -eq 'release' -and $args[1] -eq 'upload'){
        if($args[2] -ne 'update-channels' -or $args[3] -notmatch 'android-(stable|nightly)\.json$'){throw 'Android publisher touched another platform index'}
        $global:androidChannelUploads++;return
    }
    throw 'Unexpected Android channel command'
}
$savedDirectory=[Environment]::CurrentDirectory
Push-Location $fixture;[Environment]::CurrentDirectory=$fixture
try{
    $env:RESUMESTUDIO_UPDATE_SIGNING_KEY=$rsa.ExportPkcs8PrivateKeyPem();$env:GITHUB_REPOSITORY='ljy87263621/resume-studio'
    [IO.File]::WriteAllText("$fixture/release/desktop-nightly.json",'desktop-index-sentinel')
    [IO.File]::WriteAllText("$fixture/release/nightly.json",'legacy-index-sentinel')
    foreach($channel in @('nightly','stable')){
        $version='2.1.3';$tag=if($channel -eq 'stable'){'android-v2.1.3'}else{'android-nightly-20261016-aaaaaaaa'}
        $name=if($channel -eq 'stable'){$version}else{'2.1.3-nightly.20261016.aaaaaaaa'}
        $apk="$fixture/release/ResumeStudio-Android-v$name.apk";[IO.File]::WriteAllText($apk,'isolated APK fixture')
        & "$root/scripts/publish-android-release.ps1" -Channel $channel -Version $version -Commit ('a'*40) -Tag $tag -VersionName $name -VersionCode 21399999 -PackagePath $apk -SignOnly
        $envelope=Get-Content "release/android-$channel.json" -Raw|ConvertFrom-Json;$bytes=[Convert]::FromBase64String($envelope.data)
        if(!$rsa.VerifyData($bytes,[Convert]::FromBase64String($envelope.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Android signature failed'}
        $info=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
        if($info.platform -ne 'android' -or $info.version -ne $version -or $info.tag -ne $tag -or $info.androidSha256 -ne (Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant() -or $info.PSObject.Properties.Name -contains 'packageName'){throw 'Android metadata leaked desktop package identity'}
        & "$fixture/scripts/publish-android-channel.ps1" -Channel $channel
        $env:GITHUB_REPOSITORY='scj040921/resume-studio';$before=$global:androidChannelUploads;$wrongRepositoryRejected=$false
        try{& "$fixture/scripts/publish-android-channel.ps1" -Channel $channel}catch{$wrongRepositoryRejected=$true}finally{$env:GITHUB_REPOSITORY='ljy87263621/resume-studio'}
        if(!$wrongRepositoryRejected -or $global:androidChannelUploads -ne $before){throw 'Upstream repository reached Fork Android publication'}
        $global:androidChannelBadDigest=$true;$uploads=$global:androidChannelUploads;$badAssetRejected=$false
        try{& "$fixture/scripts/publish-android-channel.ps1" -Channel $channel}catch{$badAssetRejected=$true}
        if(!$badAssetRejected -or $global:androidChannelUploads -ne $uploads){throw 'Invalid APK digest was advertised'}
        $global:androidChannelBadDigest=$false
        $rejected=$false
        try{& "$root/scripts/publish-android-release.ps1" -Channel $channel -Version $version -Commit ('a'*40) -Tag 'v2.1.3' -VersionName $name -VersionCode 21399999 -PackagePath $apk -SignOnly}catch{$rejected=$true}
        if(!$rejected){throw 'Desktop tag accepted by Android publisher'}
    }
    if((Get-Content release/desktop-nightly.json -Raw) -ne 'desktop-index-sentinel' -or (Get-Content release/nightly.json -Raw) -ne 'legacy-index-sentinel'){throw 'Android signing overwrote another update index'}
    Write-Output 'PASS: Android Stable/Nightly have independent signed identities and indexes; desktop tags are rejected'
}finally{Pop-Location;[Environment]::CurrentDirectory=$savedDirectory;$env:RESUMESTUDIO_UPDATE_SIGNING_KEY=$savedKey;$env:GITHUB_REPOSITORY=$savedRepo;$rsa.Dispose();Remove-Item Function:gh}
