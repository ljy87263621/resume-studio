param([Parameter(Mandatory)][string]$PackagePath,[string]$SourceDirectory='release/ResumeStudio')
$ErrorActionPreference='Stop'
if(!$env:RESUMESTUDIO_UPDATE_SIGNING_KEY){throw 'Update signing key is not configured'}
$info=Get-Content (Join-Path $SourceDirectory 'Assets\ReleaseIdentity.json') -Raw | ConvertFrom-Json
$metadata=[ordered]@{channel=$info.channel;version=$info.version;commit=$info.commit;builtAt=$info.builtAt;tag=$info.tag;packageName=[IO.Path]::GetFileName($PackagePath);sha256=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant();packageSize=(Get-Item -LiteralPath $PackagePath).Length}
$notesRoot=Join-Path (Split-Path $PSScriptRoot -Parent) '.github/release-notes'
$notes=@(Get-ChildItem -LiteralPath $notesRoot -Filter 'v*.md'|Where-Object {$_.BaseName -match '^v\d+\.\d+\.\d+$' -and [version]$_.BaseName.Substring(1) -le [version]$info.version}|Sort-Object {[version]$_.BaseName.Substring(1)} -Descending|ForEach-Object {
    [ordered]@{version=$_.BaseName.Substring(1);body=[IO.File]::ReadAllText($_.FullName).Trim()}
})
$changeLog=[ordered]@{schema=1;channel=$info.channel;version=$info.version;commit=$info.commit;tag=$info.tag;releases=$notes}
if(!@($notes|Where-Object {$_.version -eq $info.version}).Count){throw 'Latest release changelog is missing'}
$changeLogBytes=[Text.Encoding]::UTF8.GetBytes(($changeLog|ConvertTo-Json -Depth 8))
if($changeLogBytes.Length -gt 256000){throw 'Release changelog exceeds the client limit'}
[IO.File]::WriteAllBytes('release/release-changelog.json',$changeLogBytes)
$metadata['changeLogName']='release-changelog.json'
$metadata['changeLogSha256']=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($changeLogBytes)).ToLowerInvariant()
$metadata['changeLogSize']=$changeLogBytes.Length
$installer=Join-Path (Split-Path ([IO.Path]::GetFullPath($PackagePath)) -Parent) ([IO.Path]::GetFileName($PackagePath).Replace('-windows-x64.zip','-Setup-x64.exe'))
if(Test-Path -LiteralPath $installer){
    $metadata['installerName']=[IO.Path]::GetFileName($installer)
    $metadata['installerSha256']=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $metadata['installerSize']=(Get-Item -LiteralPath $installer).Length
}
$json=($metadata | ConvertTo-Json -Depth 5)
$bytes=[Text.Encoding]::UTF8.GetBytes($json)
$rsa=[Security.Cryptography.RSA]::Create(); $rsa.ImportFromPem($env:RESUMESTUDIO_UPDATE_SIGNING_KEY)
$signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
[IO.File]::WriteAllBytes('release/release-info.json',$bytes)
[IO.File]::WriteAllText('release/release-info.sig',[Convert]::ToBase64String($signature),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(('release/'+$info.channel+'.json'),(@{data=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
$rsa.Dispose()
foreach($file in @('release/release-info.json','release/release-info.sig','release/release-changelog.json')){$hash=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(); Add-Content release/SHA256SUMS.txt "$hash  $([IO.Path]::GetFileName($file))" -Encoding utf8}
Write-Output 'Release metadata signed (RSA-PSS / SHA-256).'
