$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet).Source}
& $dotnet run --project (Join-Path $root 'tests/Workbench.Tests/Workbench.Tests.csproj') -c Release -- (Join-Path $root 'client-winui/Assets/TemplateCatalog.json')
if($LASTEXITCODE -ne 0){throw 'Workbench checks failed'}
& $dotnet run --project (Join-Path $root 'tests/Layout.Tests/Layout.Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Resume layout checks failed'}
