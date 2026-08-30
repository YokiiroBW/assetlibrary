[CmdletBinding()]
param(
  [ValidateRange(1, 96)]
  [int] $Hours = 8,
  [int] $IntervalSeconds = 30
)
$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $PSScriptRoot '..\build\bin\Release\AssetHostStub.exe'
$deadline = (Get-Date).AddHours($Hours)
$iteration = 0
Write-Host "M0-002 soak starts at $(Get-Date -Format o), ends at $($deadline.ToString('o'))."
while ((Get-Date) -lt $deadline) {
  $iteration++
  $process = Start-Process -FilePath $hostPath -ArgumentList '--once' -PassThru -WindowStyle Hidden
  Start-Sleep -Seconds ([Math]::Min($IntervalSeconds, 60))
  if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
  if (($iteration % 10) -eq 0) { Write-Host "soak iterations: $iteration" }
}
Write-Host "M0-002 soak completed with $iteration bounded host cycles."
