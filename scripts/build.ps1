param([string]$Configuration = 'Release', [string]$ReleaseDirectory = '', [ValidateSet('stable','nightly')][string]$Channel='stable', [string]$BuildTag='')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project=Get-Content (Join-Path $root 'client-winui\ResumeStudio.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$commit=(& git -C $root rev-parse HEAD).Trim()
if($commit -notmatch '^[0-9a-f]{40}$'){throw 'Cannot identify source commit'}
if(!$BuildTag){$BuildTag=if($Channel -eq 'stable'){'v'+$version}else{'nightly-'+[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('yyyyMMdd')+'-'+$commit.Substring(0,8)}}
$identity=[ordered]@{channel=$Channel;version=$version;commit=$commit;builtAt=[DateTimeOffset]::UtcNow.ToString('O');tag=$BuildTag}
[IO.File]::WriteAllText((Join-Path $root 'client-winui\Assets\ReleaseIdentity.json'),($identity|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$jdk = 'C:\Program Files\Java\jdk-21'
if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin\javac.exe'))) { $jdk = $env:JAVA_HOME }
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$classes = Join-Path $root 'backend\build\classes'
New-Item -ItemType Directory -Force $classes | Out-Null
$sources = @(Get-ChildItem (Join-Path $root 'backend\src\main\java') -Recurse -Filter '*.java' | ForEach-Object FullName)
& (Join-Path $jdk 'bin\javac.exe') -encoding UTF-8 -Xlint:all -Werror --release 21 --add-modules jdk.httpserver -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Java compile failed' }
$catalogPath=Join-Path $root 'client-winui\Assets\TemplateCatalog.json'
# The Windows Java launcher can also decode command-line arguments using the locale.
$encodedCatalogPath=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($catalogPath))
& (Join-Path $jdk 'bin\java.exe') -cp $classes com.resumestudio.TemplateCatalog --path-base64 $encodedCatalogPath
if ($LASTEXITCODE -ne 0) { throw 'Template catalogue generation failed' }
$jar = Join-Path $root 'backend\build\resume-backend.jar'
& (Join-Path $jdk 'bin\jar.exe') --create --file $jar --main-class com.resumestudio.Server -C $classes .
if ($LASTEXITCODE -ne 0) { throw 'Java package failed' }
$output = Join-Path $root "client-winui\bin\x64\$Configuration\net8.0-windows10.0.26100.0\win-x64"
$destination = if ($ReleaseDirectory) { [IO.Path]::GetFullPath($ReleaseDirectory) } else { Join-Path $root 'release\ResumeStudio' }
# Assemble into an empty, app-owned staging folder so removed NuGet dependencies cannot linger in packages.
$release = Join-Path $root ('artifacts\distribution-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $release | Out-Null
if($Configuration -eq 'Release') {
    & $dotnet publish (Join-Path $root 'client-winui\ResumeStudio.csproj') -c $Configuration -p:Platform=x64 -p:LightweightPublish=true -o $release --ignore-failed-sources
} else {
    & $dotnet build (Join-Path $root 'client-winui\ResumeStudio.csproj') -c $Configuration -p:Platform=x64 --ignore-failed-sources
    if($LASTEXITCODE -eq 0){Copy-Item -Path (Join-Path $output '*') -Destination $release -Recurse -Force}
}
if ($LASTEXITCODE -ne 0) { throw 'WinUI distribution build failed' }
# WinUI's publish output omits generated XBF / PRI files for an unpackaged project.
# Copy only those resources, never overlay the untrimmed managed DLLs from build output.
foreach($resource in Get-ChildItem -LiteralPath $output -Recurse -File|Where-Object {$_.Extension -in @('.xbf','.pri')}) {
    $target=Join-Path $release ([IO.Path]::GetRelativePath($output,$resource.FullName))
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent)|Out-Null
    Copy-Item -LiteralPath $resource.FullName -Destination $target -Force
}
foreach($name in @('App.xbf','ResumeStudio.pri','ResumeStudio.runtimeconfig.json')){if(!(Test-Path -LiteralPath (Join-Path $release $name))){throw "Missing application resource: $name"}}
New-Item -ItemType Directory -Force (Join-Path $release 'backend') | Out-Null
Copy-Item -LiteralPath $jar -Destination (Join-Path $release 'backend\resume-backend.jar') -Force
New-Item -ItemType Directory -Force (Join-Path $release 'updater') | Out-Null
foreach($helper in @('Update-App.ps1','sync-distribution.ps1','Compress-App.ps1')){
    # The updater runs in Windows PowerShell 5.1, which needs a BOM for Chinese text.
    [IO.File]::WriteAllText((Join-Path $release ('updater\'+$helper)),[IO.File]::ReadAllText((Join-Path $PSScriptRoot $helper)),[Text.UTF8Encoding]::new($true))
}
$runtime = Join-Path $release 'runtime'
# Always generate from this build's verified JDK rather than reusing an older install's runtime.
& (Join-Path $jdk 'bin\jlink.exe') --add-modules java.base,java.net.http,jdk.httpserver,jdk.crypto.ec --strip-debug --no-header-files --no-man-pages --compress=zip-9 --output $runtime
if ($LASTEXITCODE -ne 0) { throw 'Java runtime creation failed' }
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
$manifest=@(Get-ChildItem -LiteralPath $release -File -Recurse|ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($release,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[IO.File]::WriteAllText((Join-Path $release 'distribution-manifest.json'),(@{files=$manifest}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'sync-distribution.ps1') -SourceDirectory $release -DestinationDirectory $destination
$staging=[IO.Path]::GetFullPath($release)
if(!$staging.StartsWith([IO.Path]::GetFullPath((Join-Path $root 'artifacts'))+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging cleanup path' }
Remove-Item -LiteralPath $staging -Recurse -Force
Write-Output "Ready: $destination\ResumeStudio.exe"
