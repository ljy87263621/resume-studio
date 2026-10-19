param([Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$AssetsDirectory,[Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$Aapt2)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root 'artifacts/android-dependencies/ocr-libs'
New-Item -ItemType Directory -Path $cache -Force|Out-Null
$androidNs='http://schemas.android.com/apk/res/android'
[xml]$manifest=[IO.File]::ReadAllText($ManifestPath)
$application=$manifest.manifest.application
$jars=[Collections.Generic.List[string]]::new();$resources=[Collections.Generic.List[string]]::new();$packages=[Collections.Generic.HashSet[string]]::new();$native=[Collections.Generic.List[object]]::new()
$noticeDirectory=Join-Path $AssetsDirectory 'ThirdParty/AndroidOCR';New-Item -ItemType Directory -Path $noticeDirectory -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'android-ocr-dependencies.json') -Destination (Join-Path $noticeDirectory 'dependencies.json') -Force
$lock=Get-Content (Join-Path $PSScriptRoot 'android-ocr-dependencies.json') -Raw|ConvertFrom-Json
foreach($dependency in $lock){
    $path=Join-Path $cache $dependency.name
    if(!(Test-Path -LiteralPath $path)){Invoke-WebRequest $dependency.url -OutFile $path -TimeoutSec 120}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dependency.sha256){throw "Android OCR dependency checksum mismatch: $($dependency.name)"}
    if($path.EndsWith('.jar')){$jars.Add($path);continue}
    $directory=Join-Path $BuildDirectory ('ocr/'+[IO.Path]::GetFileNameWithoutExtension($path))
    New-Item -ItemType Directory -Path $directory -Force|Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($path,$directory,$true)
    if(Test-Path (Join-Path $directory 'classes.jar')){$jars.Add((Join-Path $directory 'classes.jar'))}
    if(Test-Path (Join-Path $directory 'libs')){Get-ChildItem (Join-Path $directory 'libs') -Filter '*.jar'|ForEach-Object {$jars.Add($_.FullName)}}
    if(Test-Path (Join-Path $directory 'assets')){Copy-Item -Path (Join-Path $directory 'assets/*') -Destination $AssetsDirectory -Recurse -Force}
    if(Test-Path (Join-Path $directory 'jni')){Get-ChildItem (Join-Path $directory 'jni') -Recurse -File|ForEach-Object {$native.Add(@{path=$_.FullName;name='lib/'+[IO.Path]::GetRelativePath((Join-Path $directory 'jni'),$_.FullName).Replace('\','/')})}}
    if(Test-Path (Join-Path $directory 'res')){
        $archive=Join-Path $directory 'resources.zip'; & $Aapt2 compile --dir (Join-Path $directory 'res') -o $archive
        if($LASTEXITCODE){throw "Android OCR resource compilation failed: $($dependency.name)"};$resources.Add($archive)
    }
    $libraryManifest=Join-Path $directory 'AndroidManifest.xml'
    if(Test-Path $libraryManifest){
        [xml]$library=[IO.File]::ReadAllText($libraryManifest);[void]$packages.Add([string]$library.manifest.package)
        foreach($child in @($library.manifest.application.ChildNodes)){
            if($child.NodeType -ne [Xml.XmlNodeType]::Element -or $child.LocalName -notin @('provider','service','receiver','meta-data','activity')){continue}
            $name=$child.GetAttribute('name',$androidNs);$target=@($application.ChildNodes|Where-Object {$_.LocalName -eq $child.LocalName -and $_.GetAttribute('name',$androidNs) -eq $name})|Select-Object -First 1
            if(!$target){$target=$manifest.ImportNode($child,$true);[void]$application.AppendChild($target)}else{foreach($meta in @($child.ChildNodes)){if($meta.NodeType -eq [Xml.XmlNodeType]::Element -and $meta.LocalName -eq 'meta-data'){[void]$target.AppendChild($manifest.ImportNode($meta,$true))}}}
        }
    }
}
$manifestText=$manifest.OuterXml.Replace('${applicationId}','com.resumestudio.mobile')
[IO.File]::WriteAllText($ManifestPath,$manifestText,[Text.UTF8Encoding]::new($false))
return @{jars=$jars.ToArray();resources=$resources.ToArray();packages=@($packages|Where-Object {$_});native=$native.ToArray()}
