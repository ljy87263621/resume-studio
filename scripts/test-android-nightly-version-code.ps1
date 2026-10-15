$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot 'get-android-nightly-version-code.ps1'
$project=[xml](Get-Content (Join-Path $PSScriptRoot '..\client-winui\ResumeStudio.csproj'))
$version=[version]$project.Project.PropertyGroup.Version
$prior=[int](& $script -Version $version.ToString() -DayOfYear 288 -Sequence 2)
$sameDay=[int](& $script -Version $version.ToString() -DayOfYear 288 -Sequence 3)
$nextDay=[int](& $script -Version $version.ToString() -DayOfYear 289 -Sequence 0)
$stable=[int]($version.Major*100000000+$version.Minor*1000000+$version.Build*100000+99999)
if($sameDay -le $prior){throw 'Same-day Nightly sequence did not increase'}
if($nextDay -le $sameDay){throw 'Next-day Nightly code must advance beyond every previous daily build'}
if($stable -le $nextDay){throw 'Stable should remain installable after Nightly'}
$overflowRejected=$false
try{& $script -Version '0.9.5' -DayOfYear 288 -Sequence 100|Out-Null}catch{$overflowRejected=$true}
if(!$overflowRejected){throw 'Sequence overflow was accepted'}
Write-Output "PASS: same-day Nightly $sameDay, next-day Nightly $nextDay, Stable $stable"
