$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('PlatformMigration-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$fixture/scripts","$fixture/client-winui/Assets","$fixture/source","$fixture/release"|Out-Null
Copy-Item "$PSScriptRoot/publish-platform-migration.ps1" "$fixture/scripts/"
$rsa=[Security.Cryptography.RSA]::Create(2048);$savedKey=$env:RESUMESTUDIO_UPDATE_SIGNING_KEY;$savedRepo=$env:GITHUB_REPOSITORY;$savedDirectory=[Environment]::CurrentDirectory
[IO.File]::WriteAllText("$fixture/client-winui/Assets/UpdatePublicKey.pem",$rsa.ExportSubjectPublicKeyInfoPem())
$global:migrationFixture=$fixture;$global:migrationUploads=@()
function gh {
    $global:LASTEXITCODE=0
    if($args[0] -eq 'api'){
        @(@{tag_name='nightly-20261016-aaaaaaaa';draft=$false;prerelease=$true;target_commitish=('a'*40);published_at='2026-10-16T00:00:00Z';body=''},@{tag_name='android-nightly-20261016-aaaaaaaa';draft=$false;prerelease=$true;target_commitish=('a'*40);published_at='2026-10-16T00:00:00Z';body=''})|ConvertTo-Json;return
    }
    if($args[0] -eq 'release' -and $args[1] -eq 'download'){
        $destination=$args[[Array]::IndexOf($args,'-D')+1]
        for($i=0;$i -lt $args.Count;$i++){if($args[$i] -eq '-p'){Copy-Item -LiteralPath (Join-Path "$global:migrationFixture/source" $args[$i+1]) -Destination $destination -Force}}
        return
    }
    if($args[0] -eq 'release' -and $args[1] -eq 'upload'){$global:migrationUploads+=,(@($args));return}
    throw 'Unexpected migration command'
}
Push-Location $fixture;[Environment]::CurrentDirectory=$fixture
try{
    $env:RESUMESTUDIO_UPDATE_SIGNING_KEY=$rsa.ExportPkcs8PrivateKeyPem();$env:GITHUB_REPOSITORY='ljy87263621/resume-studio'
    $apk="$fixture/source/ResumeStudio-Android-v0.9.9-nightly.20261016.aaaaaaaa.apk";[IO.File]::WriteAllText($apk,'migration APK fixture')
    & "$root/scripts/publish-android-release.ps1" -Channel nightly -Version 0.9.9 -Commit ('a'*40) -Tag android-nightly-20261016-aaaaaaaa -VersionName 0.9.9-nightly.20261016.aaaaaaaa -VersionCode 9928900 -PackagePath $apk -SignOnly
    Copy-Item release/android-release-info.* source/
    $desktop=@{platform='desktop';channel='nightly';version='0.9.9';commit=('a'*40);tag='nightly-20261016-aaaaaaaa';packageName='desktop.zip';packageSize=5;sha256=('b'*64)}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($desktop|ConvertTo-Json))
    [IO.File]::WriteAllBytes("$fixture/source/release-info.json",$bytes)
    [IO.File]::WriteAllText("$fixture/source/release-info.sig",[Convert]::ToBase64String($rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)))
    & "$fixture/scripts/publish-platform-migration.ps1" -Channel nightly -Commit ('a'*40)
    if($global:migrationUploads.Count -ne 2 -or $global:migrationUploads[0][2] -ne 'nightly-20261016-aaaaaaaa' -or $global:migrationUploads[1][2] -ne 'update-channels' -or !$global:migrationUploads[1][3].EndsWith('nightly.json') -or $global:migrationUploads[1][3].EndsWith('android-nightly.json')){throw 'Migration wrote an independent platform index'}
    if($global:migrationUploads[0] -contains 'release-info.json'){throw 'Migration overwrote desktop metadata'}
    $envelope=Get-Content release/platform-migration/nightly.json -Raw|ConvertFrom-Json;$data=[Convert]::FromBase64String($envelope.data)
    if(!$rsa.VerifyData($data,[Convert]::FromBase64String($envelope.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Migration signature failed'}
    $merged=[Text.Encoding]::UTF8.GetString($data)|ConvertFrom-Json
    if($merged.tag -ne $desktop.tag -or $merged.androidPackageName -ne (Split-Path $apk -Leaf) -or $merged.packageName -ne 'desktop.zip'){throw 'Legacy bridge lost platform package identity'}
    $global:migrationUploads=@();[IO.File]::WriteAllText("$fixture/source/android-release-info.json",'{}');$rejected=$false
    try{& "$fixture/scripts/publish-platform-migration.ps1" -Channel nightly -Commit ('a'*40)}catch{$rejected=$true}
    if(!$rejected -or $global:migrationUploads.Count){throw 'Invalid Android signature reached legacy publication'}
    Write-Output 'PASS: signed legacy bridge preserves independent indexes and desktop metadata; tampered source is rejected'
}finally{Pop-Location;[Environment]::CurrentDirectory=$savedDirectory;$env:RESUMESTUDIO_UPDATE_SIGNING_KEY=$savedKey;$env:GITHUB_REPOSITORY=$savedRepo;$rsa.Dispose();Remove-Item Function:gh}
