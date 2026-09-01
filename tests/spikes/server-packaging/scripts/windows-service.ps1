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
$MarkerText = '{"owner":"AssetLibrary-M0-004-Spike","wrapper":"' + $Wrapper.Replace('\','\\') + '","executable":"' + $Exe.Replace('\','\\') + '"}'

function Invoke-Sc([string[]]$Args) {
  & sc.exe @Args | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "sc.exe failed ($LASTEXITCODE): $($Args -join ' ')" }
}
function Get-ServiceObject {
  Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
}
function Normalize-Path([string]$Path) {
  if ($null -eq $Path) { return '' }
  return $Path.Trim().Trim('"').Trim()
}
function Test-OwnedWrapper {
  if (!(Test-Path -LiteralPath $Wrapper -PathType Leaf) -or !(Test-Path -LiteralPath $Marker -PathType Leaf)) { return $false }
  return ((Get-Content -LiteralPath $Marker -Raw) -eq $MarkerText)
}
function Assert-OwnedService {
  $service = Get-ServiceObject
  if ($null -eq $service) { throw "service not found: $Name" }
  if ((Normalize-Path $service.PathName) -ne (Normalize-Path $BinPath)) { throw 'service binPath is not owned by this adapter' }
  if (!(Test-OwnedWrapper)) { throw 'wrapper ownership marker is missing or does not match' }
  return $service
}
function Wait-ServiceState([string]$State, [int]$TimeoutSeconds = 15) {
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
  do {
    $service = Get-ServiceObject
    if ($null -eq $service) { return $false }
    if ($service.State -eq $State) { return $true }
    Start-Sleep -Milliseconds 200
  } while ([DateTime]::UtcNow -lt $deadline)
  return $false
}
function Wait-ServiceAbsent([int]$TimeoutSeconds = 15) {
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
  do {
    if ($null -eq (Get-ServiceObject)) { return $true }
    Start-Sleep -Milliseconds 200
  } while ([DateTime]::UtcNow -lt $deadline)
  return $false
}
function Assert-PortAvailable([int]$Port) {
  if (@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).Count -gt 0) { throw "port is already occupied: $Port" }
}

if ($Action -eq 'install') {
  $wrapperCreated = $false; $markerCreated = $false; $serviceCreated = $false
  try {
    if (!(Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "artifact not found: $Exe" }
    if ($null -ne (Get-ServiceObject)) { throw "service already exists: $Name" }
    if (Test-Path -LiteralPath $Wrapper -PathType Leaf -or Test-Path -LiteralPath $Marker -PathType Leaf) { throw 'service wrapper already exists; refusing to overwrite' }
    Assert-PortAvailable 5080
    New-Item -ItemType Directory -Force -Path $Data | Out-Null
    Set-Content -LiteralPath $Wrapper -Encoding ASCII -Value "@echo off`r`nset SPIKE_DATA_PATH=$Data`r`nset SPIKE_PORT=5080`r`nset SPIKE_BIND_HOST=127.0.0.1`r`ncall `"$Exe`""
    $wrapperCreated = $true
    Set-Content -LiteralPath $Marker -Encoding UTF8 -NoNewline -Value $MarkerText
    $markerCreated = $true
    Invoke-Sc @('create',$Name,'binPath=',$BinPath,'start=','demand')
    $serviceCreated = $true
    Invoke-Sc @('config',$Name,'obj=','LocalService')
    $service = Assert-OwnedService
    if ($service.StartName -notmatch '^(NT AUTHORITY\\)?LocalService$') { throw 'service account readback is not LocalService' }
  } catch {
    if ($serviceCreated -and $null -ne (Get-ServiceObject)) { try { Invoke-Sc @('delete',$Name); [void](Wait-ServiceAbsent) } catch { } }
    if ($markerCreated) { Remove-Item -LiteralPath $Marker -Force -ErrorAction SilentlyContinue }
    if ($wrapperCreated) { Remove-Item -LiteralPath $Wrapper -Force -ErrorAction SilentlyContinue }
    throw
  }
  exit 0
}
if ($Action -eq 'start') {
  [void](Assert-OwnedService); Invoke-Sc @('start',$Name)
  if (!(Wait-ServiceState 'Running')) { throw 'service did not reach Running state within timeout' }
  exit 0
}
if ($Action -eq 'stop') {
  $service = Assert-OwnedService
  if ($service.State -ne 'Stopped') { Invoke-Sc @('stop',$Name); if (!(Wait-ServiceState 'Stopped')) { throw 'service did not reach Stopped state within timeout' } }
  exit 0
}
if ($Action -eq 'uninstall') {
  $service = Get-ServiceObject
  if ($null -eq $service) {
    if (Test-OwnedWrapper) { Remove-Item -LiteralPath $Marker -Force; Remove-Item -LiteralPath $Wrapper -Force }
    exit 0
  }
  [void](Assert-OwnedService)
  if ($service.State -ne 'Stopped') { Invoke-Sc @('stop',$Name); if (!(Wait-ServiceState 'Stopped')) { throw 'service did not stop within timeout' } }
  Invoke-Sc @('delete',$Name)
  if (!(Wait-ServiceAbsent)) { throw 'service was not absent after delete' }
  Remove-Item -LiteralPath $Marker -Force
  Remove-Item -LiteralPath $Wrapper -Force
  exit 0
}
if ($Action -eq 'health') {
  if (!(Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "artifact not found: $Exe" }
  $env:SPIKE_DATA_PATH=$Data; $env:SPIKE_PORT='5080'; $env:SPIKE_BIND_HOST='127.0.0.1'; & $Exe --health-probe
  exit $LASTEXITCODE
}
