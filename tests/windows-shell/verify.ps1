[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'execution-context.ps1')
Assert-ProofNativeLaunch -Context (Get-ProofExecutionContext)
$resolvedBuild = (Resolve-Path -LiteralPath $BuildDirectory).Path
$library = Join-Path $resolvedBuild 'Release/AssetLibraryExplorerProof.dll'
$probe = Join-Path $resolvedBuild 'Release/ExplorerProofProbe.exe'
$registration = Join-Path $PSScriptRoot 'registration.ps1'
if (!(Test-Path -LiteralPath $library) -or !(Test-Path -LiteralPath $probe)) { throw 'Compiled proof artifacts are required.' }
$before = & $registration -Action verify | ConvertFrom-Json
if ($before.ClassPresent -or $before.NamespacePresent) { throw 'Proof registry must be empty before tests.' }
$registeredByThisRun = $false
try {
  $registered = & $registration -Action register -DllPath $library | ConvertFrom-Json
  $registeredByThisRun = $true
  if (!$registered.NativeLaunchRouteVerified -or $registered.RegistryView -ne 'current-process' -or !$registered.ClassPresent -or !$registered.NamespacePresent) { throw 'Registration in the selected native execution route was not confirmed.' }
  $collisionRejected = $false
  try { & $registration -Action register -DllPath $library | Out-Null }
  catch { $collisionRejected = $_.Exception.Message -eq 'Existing registration: refusing replacement.' }
  if (!$collisionRejected) { throw 'Duplicate registration was not rejected.' }
  $probeStart = [Diagnostics.ProcessStartInfo]::new($probe)
  $probeStart.UseShellExecute = $false
  $probeStart.CreateNoWindow = $true
  $probeStart.RedirectStandardOutput = $true
  $probeStart.RedirectStandardError = $true
  $evidenceDirectory = Join-Path $resolvedBuild ('verify-evidence/' + [Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $evidenceDirectory -ErrorAction Stop | Out-Null
  $stdoutPath = Join-Path $evidenceDirectory 'stdout.txt'
  $stderrPath = Join-Path $evidenceDirectory 'stderr.txt'
  $stdoutFile = $null; $stderrFile = $null; $process = $null
  $started = [DateTimeOffset]::UtcNow
  $timedOut = $false; $exitConfirmed = $false; $streamsCompleted = $false; $captureError = $null
  try {
    $stdoutFile = [IO.FileStream]::new($stdoutPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read,1,[IO.FileOptions]::WriteThrough)
    $stderrFile = [IO.FileStream]::new($stderrPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read,1,[IO.FileOptions]::WriteThrough)
    $process = [Diagnostics.Process]::Start($probeStart)
    # Preserve bytes as they arrive, including output preceding a failure/timeout.
    $output = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
    $errors = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
    $timedOut = -not $process.WaitForExit(10000)
    if ($timedOut) { $process.Kill($true); $exitConfirmed = $process.WaitForExit(3000) } else { $exitConfirmed = $true }
    $streamsCompleted = [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($output,$errors),3000)
  } catch { $captureError = $_.Exception.Message }
  finally {
    if ($process -and -not $process.HasExited) {
      try { $process.Kill($true); $exitConfirmed = $process.WaitForExit(3000) }
      catch { $captureError = 'Owned probe termination failed: ' + $_.Exception.Message }
    }
    if ($stdoutFile) { $stdoutFile.Dispose() }; if ($stderrFile) { $stderrFile.Dispose() }
    [ordered]@{ StartedUtc=$started.ToString('o');CompletedUtc=[DateTimeOffset]::UtcNow.ToString('o');Pid=if($process){$process.Id}else{$null};ExitCode=if($process -and $process.HasExited){$process.ExitCode}else{$null};TimedOut=$timedOut;ExitConfirmed=$exitConfirmed;StreamsCompleted=$streamsCompleted;CaptureError=$captureError;StdoutFile=$stdoutPath;StderrFile=$stderrPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'result.json') -Encoding utf8
  }
  try {
    if ($captureError -or -not $exitConfirmed -or -not $streamsCompleted) { throw "Isolated proof capture/exit incomplete; evidence: $evidenceDirectory" }
    if ($timedOut) { throw "Isolated proof exceeded 10 seconds; evidence: $evidenceDirectory" }
    $text = [IO.File]::ReadAllText($stdoutPath)
    $errorText = [IO.File]::ReadAllText($stderrPath)
    if ($process.ExitCode -ne 0 -or $errorText.Length -ne 0) { throw "Isolated COM/DefView probe failed; evidence: $evidenceDirectory" }
    foreach ($expected in @('CoCreateInstance=00000000','SHParseDisplayName=00000000','RootAssociationArray=00000000','RootAttributes=a8000000; Result=00000000','DesktopEnumeratesRoot=true','CreateDefView=00000000','ReadOnlyPageValidated=true')) {
      if (!$text.Contains($expected)) { throw "Missing proof result: $expected" }
    }
    Write-Output $text.Trim()
    Write-Output "Proof evidence: $evidenceDirectory"
  } finally { if ($process) { $process.Dispose() } }
} finally {
  if ($registeredByThisRun) { & $registration -Action unregister | Out-Null }
  $after = & $registration -Action verify | ConvertFrom-Json
  if ($after.ClassPresent -or $after.NamespacePresent) { throw 'Proof registration residue detected.' }
}
Write-Output 'Native-launched isolated COM/DefView + current-process registration/collision/cleanup checks passed. Actual system Explorer class, PIDL and content verification remains required; G1..G4 are not closed.'
