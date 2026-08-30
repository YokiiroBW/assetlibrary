param([ValidateSet('install','start','health','stop','uninstall')][string]$Action = 'health', [string]$ArtifactDir = '')
$ErrorActionPreference = 'Stop'
$Name = 'AssetLibrary-M0-004-Spike'
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) { $ArtifactDir = Join-Path $Root '..\..\.runtime\sandbox-storage\M0-004\artifact\win-x64' }
$Exe = Join-Path $ArtifactDir 'ServerPackagingSpike.exe'
$Data = Join-Path $env:TEMP 'AssetLibrary-M0-004-Spike-data'
$Wrapper = Join-Path $env:TEMP 'AssetLibrary-M0-004-Spike-service.cmd'
$BinPath = '"' + $Wrapper + '"'
function Invoke-Sc([string[]]$Args) { & sc.exe @Args; if ($LASTEXITCODE -ne 0) { throw "sc.exe failed ($LASTEXITCODE): $($Args -join ' ')" } }
if ($Action -eq 'install') {
  try {
    if (!(Test-Path $Exe)) { throw "artifact not found: $Exe" }
    New-Item -ItemType Directory -Force $Data | Out-Null
    Set-Content -LiteralPath $Wrapper -Encoding ASCII -Value "@echo off`r`nset SPIKE_DATA_PATH=$Data`r`nset SPIKE_PORT=5080`r`nset SPIKE_BIND_HOST=127.0.0.1`r`ncall `"$Exe`""
    Invoke-Sc @('create',$Name,'binPath=',$BinPath,'start=','demand')
    Invoke-Sc @('config',$Name,'obj=','LocalService')
  } catch { try { & sc.exe delete $Name | Out-Null } catch {}; throw }
  exit 0
}
if ($Action -eq 'start') { Invoke-Sc @('start',$Name); exit 0 }
if ($Action -eq 'stop') { Invoke-Sc @('stop',$Name); exit 0 }
if ($Action -eq 'uninstall') { try { Invoke-Sc @('stop',$Name) } catch {}; Invoke-Sc @('delete',$Name); Remove-Item -LiteralPath $Wrapper -Force -ErrorAction SilentlyContinue; exit 0 }
if ($Action -eq 'health') { $env:SPIKE_DATA_PATH=$Data; $env:SPIKE_PORT='5080'; $env:SPIKE_BIND_HOST='127.0.0.1'; & $Exe --health-probe; exit $LASTEXITCODE }
