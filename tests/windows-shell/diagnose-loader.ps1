[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$BuildDirectory,
  [Parameter(Mandatory)][string]$DllPath
)
$ErrorActionPreference = 'Stop'
$resolvedBuild = (Resolve-Path -LiteralPath $BuildDirectory).Path
$source = (Resolve-Path -LiteralPath $DllPath).Path
$probe = Join-Path $resolvedBuild 'Release/ExplorerLoaderProbe.exe'
if (!(Test-Path -LiteralPath $probe -PathType Leaf)) { throw 'Build ExplorerLoaderProbe first.' }
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
# The proof writes its diagnostic trace alongside itself. Never load another worktree's copy.
$run = Join-Path $resolvedBuild ('loader-diagnostics/' + [guid]::NewGuid().ToString('N'))
$working = Join-Path $run 'empty-working-directory'
New-Item -ItemType Directory -Path $working | Out-Null
$copy = Join-Path $run 'AssetLibraryExplorerProof.dll'
Copy-Item -LiteralPath $source -Destination $copy
if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Diagnostic copy hash differs.' }
$cases = @(
  @{ Name='default-inherited-path'; Mode='default'; SystemPath=$false; Missing=$false },
  @{ Name='default-system32-path'; Mode='default'; SystemPath=$true; Missing=$false },
  @{ Name='restricted-system32-path'; Mode='restricted'; SystemPath=$true; Missing=$false },
  @{ Name='missing-dll-negative-control'; Mode='restricted'; SystemPath=$true; Missing=$true }
)
$results = foreach ($case in $cases) {
  $start = [Diagnostics.ProcessStartInfo]::new($probe)
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.WorkingDirectory = $working
  $inputDll = if ($case.Missing) { Join-Path $run 'missing.dll' } else { $copy }
  $start.ArgumentList.Add($inputDll)
  $start.ArgumentList.Add($case.Mode)
  if ($case.SystemPath) { $start.Environment['PATH'] = [Environment]::SystemDirectory }
  $process = [Diagnostics.Process]::Start($start)
  try {
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(10000)) {
      $process.Kill($true)
      $process.WaitForExit(3000) | Out-Null
      throw 'Loader diagnostic exceeded 10 seconds.'
    }
    $text = $output.GetAwaiter().GetResult()
    if ($errors.GetAwaiter().GetResult().Length -ne 0) { throw 'Unexpected loader diagnostic stderr.' }
    $expectedExit = if ($case.Missing) { 1 } else { 0 }
    $expected = if ($case.Missing) { @('RuntimePreloaded=false','LoadLibrary=0000007e') } else {
      @('RuntimePreloaded=false','LoadLibrary=00000000','Runtime.MSVCP140.dll=System32',
        'Runtime.VCRUNTIME140.dll=System32','Runtime.VCRUNTIME140_1.dll=System32',
        'DllGetClassObject=00000000','CreateInstance=00000000')
    }
    $passed = $process.ExitCode -eq $expectedExit
    foreach ($line in $expected) { $passed = $passed -and ($text -split '\r?\n' -contains $line) }
    [pscustomobject]@{Case=$case.Name;Passed=$passed;ExitCode=$process.ExitCode;Output=$text.Trim()}
  } finally { $process.Dispose() }
}
$unchanged = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -eq $sourceHash
[pscustomobject]@{
  RecordedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
  DllSha256=$sourceHash
  SourceUnchanged=$unchanged
  Results=@($results)
  Scope='Direct DLL loading and class factory only; no registry writes or Explorer activation.'
} | ConvertTo-Json -Depth 5
if (!$unchanged -or @($results | Where-Object { !$_.Passed }).Count -ne 0) { throw 'Loader diagnostic failed; inspect case evidence.' }
