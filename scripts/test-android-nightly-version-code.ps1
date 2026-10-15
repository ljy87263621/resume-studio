$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot 'get-android-nightly-version-code.ps1'
$old=[int]9500288
$sameDay=[int](& $script -Version '0.9.5' -DayOfYear 288 -Sequence 3)
$nextDay=[int](& $script -Version '0.9.5' -DayOfYear 289 -Sequence 0)
$version=[version]'0.9.5'
$stable=[int]($version.Major*100000000+$version.Minor*1000000+$version.Build*100000+99999)
if($sameDay -le $old){throw "Same-day Nightly did not advance beyond $old"}
if($nextDay -le $sameDay){throw 'Next-day Nightly code must advance beyond every previous daily build'}
if($stable -le $nextDay){throw 'Stable should remain installable after Nightly'}
$overflowRejected=$false
try{& $script -Version '0.9.5' -DayOfYear 288 -Sequence 100|Out-Null}catch{$overflowRejected=$true}
if(!$overflowRejected){throw 'Sequence overflow was accepted'}
Write-Output "PASS: same-day Nightly $sameDay, next-day Nightly $nextDay, Stable $stable"
