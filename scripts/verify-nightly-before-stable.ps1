param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Repository='ljy87263621/resume-studio'
)
$ErrorActionPreference='Stop'
$runs=gh api "repos/$Repository/actions/workflows/nightly.yml/runs?head_sha=$Commit&status=success&per_page=10"|ConvertFrom-Json
if($LASTEXITCODE){throw 'Cannot verify Nightly workflow status'}
if(!@($runs.workflow_runs|Where-Object {$_.head_sha -eq $Commit -and $_.conclusion -eq 'success'}).Count){throw 'Publish and verify Nightly for this commit before publishing Stable.'}
$releases=gh api "repos/$Repository/releases?per_page=100"|ConvertFrom-Json
if($LASTEXITCODE){throw 'Cannot verify Nightly release'}
$candidate=@($releases|Where-Object {$_.prerelease -and !$_.draft -and $_.tag_name -match '^nightly-' -and $_.body.Contains("source-commit: $Commit")}|Sort-Object published_at -Descending|Select-Object -First 1)
if(!$candidate.Count){throw 'The successful Nightly workflow has no published release for this commit.'}
$root=Split-Path $PSScriptRoot -Parent
$evidence=Join-Path $root ('artifacts/nightly-before-stable-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
gh release download $candidate[0].tag_name --repo $Repository --pattern release-info.json --pattern release-info.sig --dir $evidence
if($LASTEXITCODE){throw 'Cannot download signed Nightly release metadata'}
$bytes=[IO.File]::ReadAllBytes((Join-Path $evidence 'release-info.json'))
$rsa=[Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $root 'client-winui/Assets/UpdatePublicKey.pem')))
    $signature=[Convert]::FromBase64String([IO.File]::ReadAllText((Join-Path $evidence 'release-info.sig')).Trim())
    if(!$rsa.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Nightly release signature is invalid'}
} finally {$rsa.Dispose()}
$info=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
if($info.channel -ne 'nightly' -or $info.commit -ne $Commit -or $info.version -ne $Version -or $info.tag -ne $candidate[0].tag_name){throw 'Nightly version and source commit must match the Stable candidate.'}
foreach($pair in @(@($info.packageName,$info.packageSize,$info.sha256),@($info.installerName,$info.installerSize,$info.installerSha256))){
    $asset=@($candidate[0].assets|Where-Object name -eq $pair[0])
    if($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -ne $pair[1] -or $asset[0].digest -ne ('sha256:'+$pair[2])){throw ('Nightly asset validation failed: '+$pair[0])}
}
Write-Output "Verified Nightly $($info.tag) for Stable v$Version ($Commit)."
