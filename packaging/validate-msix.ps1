<#
.SYNOPSIS
  Runs the Windows App Certification Kit against a built MSIX (run from an elevated prompt).
.EXAMPLE
  pwsh packaging/validate-msix.ps1 -Package packaging/out/GBDeliveryCalc_1.0.0.0_x64.msix
#>
param([Parameter(Mandatory)] [string] $Package)
$ErrorActionPreference = 'Stop'
$appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
if (-not (Test-Path $appcert)) { throw "appcert.exe not found. Install the Windows SDK with the App Certification Kit feature." }
$report = Join-Path (Split-Path $Package) 'wack-report.xml'
& $appcert reset
& $appcert test -appxpackagepath (Resolve-Path $Package) -reportoutputpath $report
Write-Host "WACK report: $report (open it and confirm OVERALL_RESULT=PASS)"
