param([Parameter(Mandatory)][string]$PackagePath,[string]$SourceDirectory='release/ResumeStudio')
$ErrorActionPreference='Stop'
if(!$env:RESUMESTUDIO_UPDATE_SIGNING_KEY){throw 'Update signing key is not configured'}
$info=Get-Content (Join-Path $SourceDirectory 'Assets\ReleaseIdentity.json') -Raw | ConvertFrom-Json
$metadata=[ordered]@{channel=$info.channel;version=$info.version;commit=$info.commit;builtAt=$info.builtAt;tag=$info.tag;packageName=[IO.Path]::GetFileName($PackagePath);sha256=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant();packageSize=(Get-Item -LiteralPath $PackagePath).Length}
$json=($metadata | ConvertTo-Json -Depth 5)
$bytes=[Text.Encoding]::UTF8.GetBytes($json)
$rsa=[Security.Cryptography.RSA]::Create(); $rsa.ImportFromPem($env:RESUMESTUDIO_UPDATE_SIGNING_KEY)
$signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
[IO.File]::WriteAllBytes('release/release-info.json',$bytes)
[IO.File]::WriteAllText('release/release-info.sig',[Convert]::ToBase64String($signature),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(('release/'+$info.channel+'.json'),(@{data=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
$rsa.Dispose()
foreach($file in @('release/release-info.json','release/release-info.sig')){$hash=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(); Add-Content release/SHA256SUMS.txt "$hash  $([IO.Path]::GetFileName($file))" -Encoding utf8}
Write-Output 'Release metadata signed (RSA-PSS / SHA-256).'
