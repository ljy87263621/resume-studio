param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidateRange(1,366)][int]$DayOfYear,
    [Parameter(Mandatory)][ValidateRange(0,99)][int]$Sequence
)
$ErrorActionPreference='Stop'
$parts=$Version.Split('.')
$base=[int64]$parts[0]*100000000+[int64]$parts[1]*1000000+[int64]$parts[2]*100000
$code=$base+[int64]$DayOfYear*100+$Sequence
$stableCode=$base+99999
if($code -ge $stableCode -or $code -gt [int]::MaxValue){throw 'Nightly Android version code must increase within the day and stay below Stable'}
[int]$code
