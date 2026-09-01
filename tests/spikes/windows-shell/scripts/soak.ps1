[CmdletBinding()]
param(
  [ValidateRange(1, 96)]
  [int] $Hours = 8,
  [ValidateRange(1, 300)]
  [int] $IntervalSeconds = 30,
  [ValidateRange(1, 12000)]
  [int] $MaxIterations = 1000,
  [ValidateRange(1, 300)]
  [int] $SchedulingGapSeconds = 60
)
# This is intentionally a host-cycle helper, not an Explorer soak. The manual
# Explorer protocol in docs/spikes/M0-002/explorer-soak-protocol.md supplies the
# namespace navigation and bounded-response evidence that this helper cannot.
$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $PSScriptRoot '..\build\bin\Release\AssetHostStub.exe'
$startWallClock = Get-Date
$expectedEnd = $startWallClock.AddHours($Hours)
$runDuration = [TimeSpan]::FromHours($Hours)
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$iteration = 0
if (@(Get-Process -Name 'AssetHostStub' -ErrorAction SilentlyContinue).Count -gt 0) {
  throw 'M0-002 host-cycle helper refuses to start while an AssetHostStub process already exists.'
}
Write-Host "M0-002 host-cycle helper starts at $($startWallClock.ToString('o')), expected end $($expectedEnd.ToString('o'))."
while ($stopwatch.Elapsed -lt $runDuration -and $iteration -lt $MaxIterations) {
  $roundStart = $stopwatch.Elapsed
  $process = $null
  $controlledStop = $false
  try {
    # Keep one host per cycle; cleanup is bounded so a hung host cannot accumulate.
    $process = Start-Process -FilePath $hostPath -ArgumentList '--once' -PassThru -WindowStyle Hidden
    $intervalStart = $stopwatch.Elapsed
    Start-Sleep -Seconds $IntervalSeconds
    $observedInterval = $stopwatch.Elapsed - $intervalStart
    if ($observedInterval.TotalSeconds -gt ($IntervalSeconds + $SchedulingGapSeconds)) {
      throw "M0-002 scheduling gap exceeded the interval bound: $([Math]::Round($observedInterval.TotalSeconds, 1)) seconds."
    }
  }
  finally {
    if ($null -ne $process) {
      try {
        if ($process.HasExited) {
          $exitCode = $process.ExitCode
          if ($exitCode -ne 0) { throw "M0-002 host exited early with code $exitCode." }
        }
        else {
          Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
          $controlledStop = $true
        }
        if (-not $process.WaitForExit(5000)) {
          throw "M0-002 host process $($process.Id) did not exit within the 5-second cleanup bound."
        }
      }
      finally {
        $process.Dispose()
      }
    }
  }
  $iteration++
  $roundElapsed = $stopwatch.Elapsed - $roundStart
  Write-Host "soak iteration $iteration elapsed $([Math]::Round($roundElapsed.TotalSeconds, 1))s controlledStop=$controlledStop"
  if (($iteration % 10) -eq 0) { Write-Host "soak iterations: $iteration" }
}
if ($iteration -ge $MaxIterations -and $stopwatch.Elapsed -lt $runDuration) {
  throw "M0-002 host-cycle helper reached MaxIterations ($MaxIterations) before the requested deadline; 8-hour soak is incomplete."
}
Write-Host "M0-002 host-cycle helper completed with $iteration bounded host cycles."
