param([ValidateSet('install','start','health','stop','uninstall')][string]$Action = 'health', [string]$ArtifactDir = '')
$ErrorActionPreference = 'Stop'
$Name = 'AssetLibrary-M0-004-Spike'
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) { $ArtifactDir = Join-Path $Root '..\..\.runtime\sandbox-storage\M0-004\artifact\win-x64' }
$Exe = [IO.Path]::GetFullPath((Join-Path $ArtifactDir 'ServerPackagingSpike.exe'))
$Data = Join-Path $env:TEMP 'AssetLibrary-M0-004-Spike-data'
$Wrapper = Join-Path $env:TEMP 'AssetLibrary-M0-004-Spike-service.cmd'
$Marker = "$Wrapper.owner.json"
$BinPath = '"' + $Wrapper + '"'
$WrapperText = "@echo off`r`nset SPIKE_DATA_PATH=$Data`r`nset SPIKE_PORT=5080`r`nset SPIKE_BIND_HOST=127.0.0.1`r`ncall `"$Exe`""
$MarkerText = '{"owner":"AssetLibrary-M0-004-Spike","wrapper":"' + $Wrapper.Replace('\','\\') + '","executable":"' + $Exe.Replace('\','\\') + '"}'
function Invoke-Sc([string[]]$Args) {
  & sc.exe @Args | Out-Null
  if ($LASTEXITCODE -ne 0) {
    throw "sc.exe failed ($LASTEXITCODE): $($Args -join ' ')"
  }
}
function Get-ServiceObject {
  Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
}
function Normalize-Path([string]$Path) {
  if ($null -eq $Path) {
    return ''
  }
  return $Path.Trim().Trim('"').Trim()
}
function Test-OwnedWrapper {
  if (!(Test-Path -LiteralPath $Wrapper -PathType Leaf) -or !(Test-Path -LiteralPath $Marker -PathType Leaf)) { return $false }
  return ((Get-Content -LiteralPath $Marker -Raw) -eq $MarkerText -and (Get-Content -LiteralPath $Wrapper -Raw) -eq $WrapperText)
}
function Assert-OwnedService {
  $service = Get-ServiceObject
  if ($null -eq $service) { throw "service not found: $Name" }
  if ((Normalize-Path $service.PathName) -ne (Normalize-Path $BinPath)) { throw 'service binPath is not owned by this adapter' }
  if (!(Test-OwnedWrapper)) { throw 'wrapper ownership marker or content does not match' }
  if ($service.StartName -notmatch '^(NT AUTHORITY\\)?LocalService$') { throw 'service account is not LocalService' }
  return $service
}
function Wait-ServiceState([string]$State, [int]$TimeoutSeconds = 15) {
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
  do { $service = Get-ServiceObject; if ($null -eq $service) { return $false }; if ($service.State -eq $State) { return $true }; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $deadline)
  return $false
}
function Wait-ServiceAbsent([int]$TimeoutSeconds = 15) {
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
  do { if ($null -eq (Get-ServiceObject)) { return $true }; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $deadline)
  return $false
}
function Assert-PortAvailable([int]$Port) {
  $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
  if ($listeners.Count -gt 0) {
    throw "port is already occupied: $Port"
  }
}
function Remove-OwnedDataIfEmpty {
  if (!(Test-Path -LiteralPath $Data -PathType Container)) { return }
  $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\\')
  $dataPath = [IO.Path]::GetFullPath($Data).TrimEnd('\\')
  if ((Split-Path -Parent $dataPath).TrimEnd('\\') -ne $tempRoot) { return }
  if (@(Get-ChildItem -LiteralPath $Data -Force -ErrorAction Stop).Count -eq 0) { Remove-Item -LiteralPath $Data -Force }
}
if ($Action -eq 'install') {
  $wrapperCreated = $false; $markerCreated = $false; $serviceCreated = $false; $dataCreated = $false
  try {
    if (!(Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "artifact not found: $Exe" }
    if ($null -ne (Get-ServiceObject)) { throw "service already exists: $Name" }
    if ((Test-Path -LiteralPath $Wrapper -PathType Leaf) -or (Test-Path -LiteralPath $Marker -PathType Leaf)) { throw 'service wrapper already exists; refusing to overwrite' }
    Assert-PortAvailable 5080
    $dataCreated = !(Test-Path -LiteralPath $Data -PathType Container)
    New-Item -ItemType Directory -Force -Path $Data | Out-Null
    Set-Content -LiteralPath $Wrapper -Encoding ASCII -NoNewline -Value $WrapperText
    $wrapperCreated = $true
    Set-Content -LiteralPath $Marker -Encoding UTF8 -NoNewline -Value $MarkerText
    $markerCreated = $true
    Invoke-Sc @('create',$Name,'binPath=',$BinPath,'start=','demand')
    $serviceCreated = $true
    Invoke-Sc @('config',$Name,'obj=','LocalService')
    [void](Assert-OwnedService)
  } catch {
    $installError = $_
    $serviceAbsent = $true
    if ($serviceCreated -and $null -ne (Get-ServiceObject)) {
      try {
        Invoke-Sc @('delete',$Name)
        $serviceAbsent = Wait-ServiceAbsent
      } catch {
        $serviceAbsent = $false
      }
      if (!$serviceAbsent) {
        throw 'install cleanup incomplete; service still exists, wrapper and marker were retained'
      }
    }
    if ($serviceAbsent) {
      if ($markerCreated) { Remove-Item -LiteralPath $Marker -Force -ErrorAction SilentlyContinue }
      if ($wrapperCreated) { Remove-Item -LiteralPath $Wrapper -Force -ErrorAction SilentlyContinue }
      if ($dataCreated) { Remove-OwnedDataIfEmpty }
    }
    throw $installError
  }
  exit 0
}
if ($Action -eq 'start') {
  $service = Assert-OwnedService
  Invoke-Sc @('start',$Name)
  $service = Assert-OwnedService
  if (!(Wait-ServiceState 'Running')) {
    throw 'service did not reach Running state within timeout'
  }
  exit 0
}
if ($Action -eq 'stop') {
  $service = Assert-OwnedService
  if ($service.State -ne 'Stopped') {
    $service = Assert-OwnedService
    Invoke-Sc @('stop',$Name)
    $service = Assert-OwnedService
    if (!(Wait-ServiceState 'Stopped')) {
      throw 'service did not reach Stopped state within timeout'
    }
  }
  exit 0
}
if ($Action -eq 'uninstall') {
  $service = Get-ServiceObject
  if ($null -eq $service) {
    $wrapperExists = Test-Path -LiteralPath $Wrapper -PathType Leaf
    $markerExists = Test-Path -LiteralPath $Marker -PathType Leaf
    if (!$wrapperExists -and !$markerExists) {
      Remove-OwnedDataIfEmpty
      exit 0
    }
    if (!$wrapperExists -or !$markerExists -or !(Test-OwnedWrapper)) {
      throw 'service absent but wrapper ownership state is partial or unknown'
    }
    Remove-Item -LiteralPath $Marker -Force
    Remove-Item -LiteralPath $Wrapper -Force
    Remove-OwnedDataIfEmpty
    exit 0
  }
  $service = Assert-OwnedService
  if ($service.State -ne 'Stopped') {
    $service = Assert-OwnedService
    Invoke-Sc @('stop',$Name)
    $service = Assert-OwnedService
    if (!(Wait-ServiceState 'Stopped')) {
      throw 'service did not stop within timeout'
    }
  }
  $service = Assert-OwnedService
  Invoke-Sc @('delete',$Name)
  if (!(Wait-ServiceAbsent)) {
    throw 'service was not absent after delete'
  }
  Remove-Item -LiteralPath $Marker -Force
  Remove-Item -LiteralPath $Wrapper -Force
  Remove-OwnedDataIfEmpty
  exit 0
}
if ($Action -eq 'health') {
  if (!(Test-Path -LiteralPath $Exe -PathType Leaf)) {
    throw "artifact not found: $Exe"
  }
  $env:SPIKE_DATA_PATH = $Data
  $env:SPIKE_PORT = '5080'
  $env:SPIKE_BIND_HOST = '127.0.0.1'
  & $Exe --health-probe
  exit $LASTEXITCODE
}
