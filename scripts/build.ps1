param([string]$Configuration = 'Release', [string]$ReleaseDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$jdk = 'C:\Program Files\Java\jdk-21'
if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin\javac.exe'))) { $jdk = $env:JAVA_HOME }
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$classes = Join-Path $root 'backend\build\classes'
New-Item -ItemType Directory -Force $classes | Out-Null
$sources = @(Get-ChildItem (Join-Path $root 'backend\src\main\java') -Recurse -Filter '*.java' | ForEach-Object FullName)
& (Join-Path $jdk 'bin\javac.exe') -encoding UTF-8 -Xlint:all -Werror --release 21 --add-modules jdk.httpserver -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Java compile failed' }
$catalog = & (Join-Path $jdk 'bin\java.exe') -cp $classes com.resumestudio.TemplateCatalog
if ($LASTEXITCODE -ne 0) { throw 'Template catalogue generation failed' }
[IO.File]::WriteAllText((Join-Path $root 'client-winui\Assets\TemplateCatalog.json'), ($catalog -join "`n"), [Text.UTF8Encoding]::new($false))
$jar = Join-Path $root 'backend\build\resume-backend.jar'
& (Join-Path $jdk 'bin\jar.exe') --create --file $jar --main-class com.resumestudio.Server -C $classes .
if ($LASTEXITCODE -ne 0) { throw 'Java package failed' }
& $dotnet build (Join-Path $root 'client-winui\ResumeStudio.csproj') -c $Configuration -p:Platform=x64 --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed' }
$output = Join-Path $root "client-winui\bin\x64\$Configuration\net8.0-windows10.0.26100.0\win-x64"
$release = if ($ReleaseDirectory) { [IO.Path]::GetFullPath($ReleaseDirectory) } else { Join-Path $root 'release\ResumeStudio' }
New-Item -ItemType Directory -Force $release | Out-Null
Copy-Item -Path (Join-Path $output '*') -Destination $release -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $release 'backend') | Out-Null
Copy-Item -LiteralPath $jar -Destination (Join-Path $release 'backend\resume-backend.jar') -Force
$runtime = Join-Path $release 'runtime'
if (!(Test-Path (Join-Path $runtime 'bin\java.exe'))) {
    & (Join-Path $jdk 'bin\jlink.exe') --add-modules java.base,java.net.http,jdk.httpserver,jdk.crypto.ec --strip-debug --no-header-files --no-man-pages --compress=2 --output $runtime
    if ($LASTEXITCODE -ne 0) { throw 'Java runtime creation failed' }
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'RUNTIME_NOTICES.md') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'ThirdParty') -Destination $release -Recurse -Force
if(Test-Path (Join-Path $root 'docs')) { Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $release -Recurse -Force }
# Preserve redistribution terms from the exact NuGet versions resolved by this build.
$assets = Get-Content (Join-Path $root 'client-winui\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$licensePackages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($download in $framework.Value.downloadDependencies) {
        $licensePackages += $download.name + '/' + $download.version.Trim('[',']').Split(',')[0].Trim()
    }
}
foreach ($libraryName in ($licensePackages | Select-Object -Unique)) {
    foreach ($packageRoot in $packageRoots) {
        $packageDir = Join-Path $packageRoot $libraryName.ToLowerInvariant()
        if (!(Test-Path $packageDir)) { continue }
        $noticeFiles = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '(?i)license|notice|copying' })
        if ($noticeFiles.Count -gt 0) {
            $licenseDir = Join-Path $release ('licenses\' + $libraryName.Replace('/','-'))
            New-Item -ItemType Directory -Force $licenseDir | Out-Null
            $noticeFiles | Copy-Item -Destination $licenseDir -Force
        }
    }
}
# Runtime pack ships .NET's license and third-party notices; do not use machine SDK stubs.
$dotnetLicenses = Get-ChildItem (Join-Path $release 'licenses') -Directory | Where-Object { $_.Name -like 'Microsoft.NETCore.App.Runtime.win-x64-*' -or $_.Name -like 'microsoft.netcore.app.runtime.win-x64-*' }
if (!$dotnetLicenses) { throw 'Missing .NET runtime redistribution notices' }
Write-Output "Ready: $release\ResumeStudio.exe"
