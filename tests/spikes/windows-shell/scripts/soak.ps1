[CmdletBinding()]
param(
  [ValidateRange(1, 96)]
  [int] $Hours = 8,
  [ValidateRange(1, 300)]
  [int] $IntervalSeconds = 30,
  [ValidateRange(1, 100000)]
  [int] $MaxIterations = 1000
)
# This is intentionally a host-cycle helper, not an Explorer soak. The manual
# Explorer protocol in docs/spikes/M0-002/explorer-soak-protocol.md supplies the
# namespace navigation and bounded-response evidence that this helper cannot.
$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $PSScriptRoot '..\build\bin\Release\AssetHostStub.exe'
$deadline = (Get-Date).AddHours($Hours)
$iteration = 0
Write-Host "M0-002 host-cycle helper starts at $(Get-Date -Format o), ends at $($deadline.ToString('o'))."
while ((Get-Date) -lt $deadline -and $iteration -lt $MaxIterations) {
  $iteration++
  $process = $null
  try {
    # Keep one host per cycle; cleanup is bounded so a hung host cannot accumulate.
    $process = Start-Process -FilePath $hostPath -ArgumentList '--once' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds $IntervalSeconds
  }
  finally {
    if ($null -ne $process) {
      try {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        [void]$process.WaitForExit(5000)
      }
      finally {
        $process.Dispose()
      }
    }
  }
  if (($iteration % 10) -eq 0) { Write-Host "soak iterations: $iteration" }
}
if ($iteration -ge $MaxIterations -and (Get-Date) -lt $deadline) {
  throw "M0-002 host-cycle helper reached MaxIterations ($MaxIterations) before the requested deadline; 8-hour soak is incomplete."
}
Write-Host "M0-002 host-cycle helper completed with $iteration bounded host cycles."
