[CmdletBinding()]
param(
  [ValidateRange(1, 96)]
  [int] $Hours = 8,
  [int] $IntervalSeconds = 30
)
# This is intentionally a host-cycle helper, not an Explorer soak. The manual
# Explorer protocol in docs/spikes/M0-002/explorer-soak-protocol.md supplies the
# namespace navigation and bounded-response evidence that this helper cannot.
$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $PSScriptRoot '..\build\bin\Release\AssetHostStub.exe'
$deadline = (Get-Date).AddHours($Hours)
$iteration = 0
Write-Host "M0-002 host-cycle helper starts at $(Get-Date -Format o), ends at $($deadline.ToString('o'))."
while ((Get-Date) -lt $deadline) {
  $iteration++
  $process = Start-Process -FilePath $hostPath -ArgumentList '--once' -PassThru -WindowStyle Hidden
  Start-Sleep -Seconds ([Math]::Min($IntervalSeconds, 60))
  if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
  if (($iteration % 10) -eq 0) { Write-Host "soak iterations: $iteration" }
}
Write-Host "M0-002 host-cycle helper completed with $iteration bounded host cycles."
