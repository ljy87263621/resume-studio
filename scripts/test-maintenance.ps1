$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet).Source}
& $dotnet run --project (Join-Path $root 'tests/Maintenance.Tests/Maintenance.Tests.csproj') -c Release -- (Join-Path $root 'scripts/Compress-App.ps1')
if($LASTEXITCODE -ne 0){throw 'Storage maintenance and changelog verification failed'}
