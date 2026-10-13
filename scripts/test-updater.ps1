$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if(!(Test-Path $dotnet)){$dotnet=(Get-Command dotnet).Source}
& $dotnet run --project (Join-Path $root 'tests/Updater.Tests/Updater.Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Updater verification failed'}
& (Join-Path $PSScriptRoot 'test-update-install.ps1')
