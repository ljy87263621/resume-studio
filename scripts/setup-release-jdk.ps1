param([string]$DestinationDirectory = '')
$ErrorActionPreference = 'Stop'
$destination = if ($DestinationDirectory) { [IO.Path]::GetFullPath($DestinationDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) 'ResumeStudio-OpenJDK-21.0.12.1' }
New-Item -ItemType Directory -Force $destination | Out-Null
$zip = Join-Path $destination 'microsoft-jdk-21.0.12.1-windows-x64.zip'
if (!(Test-Path $zip)) {
    Invoke-WebRequest 'https://aka.ms/download-jdk/microsoft-jdk-21.0.12.1-windows-x64.zip' -OutFile $zip
}
$expected = '192441a9d27da813bada974bb88b4cf64d37a9589ed37f204374d411ca5ce07f'
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Microsoft OpenJDK checksum mismatch' }
$jdk = Join-Path $destination 'jdk-21.0.12.1+1'
if (!(Test-Path (Join-Path $jdk 'bin\javac.exe'))) { Expand-Archive -LiteralPath $zip -DestinationPath $destination -Force }
$env:JAVA_HOME = $jdk
if ($env:GITHUB_ENV) { Add-Content -LiteralPath $env:GITHUB_ENV -Value "JAVA_HOME=$jdk" -Encoding utf8 }
Write-Output "Verified release JDK: $jdk"
