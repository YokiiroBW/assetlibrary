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
  $process = [Diagnostics.Process]::Start($probeStart)
  try {
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(10000)) { $process.Kill($true); $process.WaitForExit(3000) | Out-Null; throw 'Isolated proof exceeded 10 seconds.' }
    $text = $output.GetAwaiter().GetResult()
    $errorText = $errors.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0 -or $errorText.Length -ne 0) { throw 'Isolated COM/DefView probe failed.' }
    foreach ($expected in @('CoCreateInstance=00000000','SHParseDisplayName=00000000','RootAssociationArray=00000000','RootAttributes=a8000000; Result=00000000','DesktopEnumeratesRoot=true','CreateDefView=00000000','BindChild=00000000')) {
      if (!$text.Contains($expected)) { throw "Missing proof result: $expected" }
    }
    Write-Output $text.Trim()
  } finally { $process.Dispose() }
} finally {
  if ($registeredByThisRun) { & $registration -Action unregister | Out-Null }
  $after = & $registration -Action verify | ConvertFrom-Json
  if ($after.ClassPresent -or $after.NamespacePresent) { throw 'Proof registration residue detected.' }
}
Write-Output 'Native-launched isolated COM/DefView + current-process registration/collision/cleanup checks passed. Actual system Explorer class, PIDL and content verification remains required; G1..G4 are not closed.'
