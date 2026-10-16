$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path $PSScriptRoot -Parent
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('ResumeChannel-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'client-winui/Assets'),(Join-Path $fixtureRoot 'release'),(Join-Path $fixtureRoot 'source/Assets') -Force|Out-Null
$savedKey=$env:RESUMESTUDIO_UPDATE_SIGNING_KEY;$savedRepo=$env:GITHUB_REPOSITORY;$savedSha=$env:GITHUB_SHA
$savedWorkingDirectory=[Environment]::CurrentDirectory
$rsa=[Security.Cryptography.RSA]::Create(3072)
$global:resumeChannelUploads=0;$global:resumeChannelCreated=$false;$global:resumeChannelWrongSize=$false
function gh {
    $global:LASTEXITCODE=0
    if($args[0] -ne 'release'){throw 'Unexpected mock command'}
    switch($args[1]){
        'view' {
            if($args[2] -eq 'update-channels'){
                if(!$global:resumeChannelCreated){$global:LASTEXITCODE=1;return}
                '{"tagName":"update-channels"}';return
            }
            $size=if($global:resumeChannelWrongSize){$global:resumeChannelFixtureSize+1}else{$global:resumeChannelFixtureSize}
            @{tagName='v0.6.0';isPrerelease=$false;assets=@(@{name='ResumeStudio-v0.6.0-windows-x64.zip';size=$size},@{name='release-info.json'},@{name='release-info.sig'},@{name='release-changelog.json';size=(Get-Item release/release-changelog.json).Length})}|ConvertTo-Json -Depth 5
        }
        'create' {$global:resumeChannelCreated=$true}
        'upload' {$global:resumeChannelUploads++}
        default {throw 'Unexpected mock release command'}
    }
}
Push-Location $fixtureRoot
[Environment]::CurrentDirectory=$fixtureRoot
try {
    $env:RESUMESTUDIO_UPDATE_SIGNING_KEY=$rsa.ExportPkcs8PrivateKeyPem();$env:GITHUB_REPOSITORY='scj040921/resume-studio';$env:GITHUB_SHA='a'*40
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'client-winui/Assets/UpdatePublicKey.pem'),$rsa.ExportSubjectPublicKeyInfoPem())
    @{channel='stable';version='0.6.0';commit=('a'*40);builtAt='2026-10-13T00:00:00Z';tag='v0.6.0'}|ConvertTo-Json|Set-Content source/Assets/ReleaseIdentity.json -Encoding utf8
    $package=Join-Path $fixtureRoot 'release/ResumeStudio-v0.6.0-windows-x64.zip';[IO.File]::WriteAllBytes($package,[Text.Encoding]::UTF8.GetBytes('isolated channel fixture'))
    $global:resumeChannelFixtureSize=(Get-Item -LiteralPath $package).Length
    $installer=Join-Path $fixtureRoot 'release/ResumeStudio-v0.6.0-Setup-x64.exe'
    [IO.File]::WriteAllBytes($installer,[Text.Encoding]::UTF8.GetBytes('isolated installer fixture'))
    & (Join-Path $repositoryRoot 'scripts/sign-release.ps1') -PackagePath $package -SourceDirectory (Join-Path $fixtureRoot 'source')
    & (Join-Path $repositoryRoot 'scripts/publish-channel.ps1') -Channel stable
    $envelope=Get-Content release/desktop-stable.json -Raw|ConvertFrom-Json;$data=[Convert]::FromBase64String($envelope.data)
    if($global:resumeChannelUploads -ne 1 -or !$rsa.VerifyData($data,[Convert]::FromBase64String($envelope.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss) -or [Text.Encoding]::UTF8.GetString($data) -ne [IO.File]::ReadAllText((Join-Path $fixtureRoot 'release/release-info.json'))){throw 'Single-file channel changed signed bytes'}
    Write-Output 'PASS: actual signing and publication scripts preserve the signed payload in one envelope'
    $signedInfo=[Text.Encoding]::UTF8.GetString($data)|ConvertFrom-Json
    if($signedInfo.installerName -ne 'ResumeStudio-v0.6.0-Setup-x64.exe' -or $signedInfo.installerSize -ne (Get-Item -LiteralPath $installer).Length -or $signedInfo.installerSha256 -ne (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Installer digest is missing from signed metadata'}
    Write-Output 'PASS: Setup filename, size and SHA-256 are included in the signed metadata'
    $global:resumeChannelWrongSize=$true;$global:resumeChannelUploads=0;$rejected=$false
    try{& (Join-Path $repositoryRoot 'scripts/publish-channel.ps1') -Channel stable}catch{$rejected=$true}
    if(!$rejected -or $global:resumeChannelUploads -ne 0){throw 'Wrong published package size reached channel upload'}
    Write-Output 'PASS: a package size mismatch prevents channel publication'
    $global:resumeChannelWrongSize=$false
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'release/release-info.json'),'{}')
    $rejected=$false;try{& (Join-Path $repositoryRoot 'scripts/publish-channel.ps1') -Channel stable}catch{$rejected=$true}
    if(!$rejected -or $global:resumeChannelUploads -ne 0){throw 'Corrupt metadata reached channel upload'}
    Write-Output 'PASS: invalid metadata signature prevents channel publication'
} finally {
    Pop-Location;[Environment]::CurrentDirectory=$savedWorkingDirectory;$rsa.Dispose();$env:RESUMESTUDIO_UPDATE_SIGNING_KEY=$savedKey;$env:GITHUB_REPOSITORY=$savedRepo;$env:GITHUB_SHA=$savedSha
    Remove-Item Function:gh
}
exit 0
