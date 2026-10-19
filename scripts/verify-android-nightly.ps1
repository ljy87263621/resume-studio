param([Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,[Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference='Stop'
$repository='ljy87263621/resume-studio'
$runs=gh api "repos/$repository/actions/workflows/android-release.yml/runs?head_sha=$Commit&status=success&per_page=100"|ConvertFrom-Json
if($LASTEXITCODE -or !@($runs.workflow_runs|Where-Object {$_.head_sha -eq $Commit -and $_.conclusion -eq 'success'}).Count){throw 'Publish a successful Android Nightly for this exact source first'}
$releases=gh api "repos/$repository/releases?per_page=100"|ConvertFrom-Json
if($LASTEXITCODE){throw 'Cannot query Android releases'}
$release=@($releases|Where-Object {$_.prerelease -and !$_.draft -and $_.tag_name -match '^android-nightly-' -and $_.body.Contains("source-commit: $Commit")}|Sort-Object published_at -Descending|Select-Object -First 1)
if(!$release.Count){throw 'Matching Android Nightly is missing'}
$evidence=Join-Path ([IO.Path]::GetTempPath()) ('AndroidNightly-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $evidence|Out-Null
gh release download $release[0].tag_name --repo $repository -p android-release-info.json -p android-release-info.sig -D $evidence
if($LASTEXITCODE){throw 'Cannot download Android Nightly metadata'}
$bytes=[IO.File]::ReadAllBytes((Join-Path $evidence 'android-release-info.json'))
$rsa=[Security.Cryptography.RSA]::Create()
try{
    $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../client-winui/Assets/UpdatePublicKey.pem')))
    if(!$rsa.VerifyData($bytes,[Convert]::FromBase64String((Get-Content (Join-Path $evidence 'android-release-info.sig') -Raw).Trim()),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Invalid Android Nightly signature'}
}finally{$rsa.Dispose()}
$info=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
if($info.platform -ne 'android' -or $info.channel -ne 'nightly' -or $info.version -ne $Version -or $info.commit -ne $Commit -or $info.tag -ne $release[0].tag_name -or !$info.tag.EndsWith($Commit.Substring(0,8))){throw 'Android Nightly identity does not match Stable candidate'}
$asset=@($release[0].assets|Where-Object name -eq $info.androidPackageName)
if($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -ne $info.androidPackageSize -or $asset[0].digest -ne ('sha256:'+$info.androidSha256)){throw 'Android Nightly APK digest mismatch'}
Write-Output "Verified Android Nightly $($info.tag) for Stable $Version"
