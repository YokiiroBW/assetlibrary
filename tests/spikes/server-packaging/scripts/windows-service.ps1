param([ValidateSet('install','start','health','stop','uninstall')][string]$Action = 'health', [string]$ArtifactDir = '')
$ErrorActionPreference = 'Stop'
$Name = 'AssetLibrary-M0-004-Spike'
$Owner = 'AssetLibrary-M0-004-Spike'
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) { $ArtifactDir = Join-Path $Root '..\..\.runtime\sandbox-storage\M0-004\artifact\win-x64' }
$SourceExe = [IO.Path]::GetFullPath((Join-Path $ArtifactDir 'ServerPackagingSpike.exe'))
$StageRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'Temp\AssetLibrary-M0-004-Spike'))
$StageApp = Join-Path $StageRoot 'app'
$Data = Join-Path $StageRoot 'data'
$StageExe = Join-Path $StageApp 'ServerPackagingSpike.exe'
$Marker = Join-Path $StageRoot 'owner.json'
$BinPath = '"' + $StageExe + '" --spike-data-path "' + $Data + '" --spike-port "5080" --spike-bind-host "127.0.0.1"'
$MutexName = 'Global\AssetLibrary-M0-004-Spike'

function Invoke-Sc([string[]]$Args) {
  & sc.exe @Args | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "sc.exe failed ($LASTEXITCODE): $($Args -join ' ')" }
}
function Get-ServiceObject { Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue }
function Get-Marker {
  if (!(Test-Path -LiteralPath $Marker -PathType Leaf)) { throw 'owner marker is missing' }
  $value = Get-Content -LiteralPath $Marker -Raw | ConvertFrom-Json
  if ($value.owner -ne $Owner -or [string]::IsNullOrWhiteSpace($value.install_id)) { throw 'owner marker is invalid' }
  if ($value.bin_path -ne $BinPath -or $value.data_path -ne $Data) { throw 'owner marker paths do not match canonical staging' }
  return $value
}
function Assert-OwnedService {
  $service = Get-ServiceObject
  if ($null -eq $service) { throw "service not found: $Name" }
  $marker = Get-Marker
  if ($service.PathName -ne $marker.bin_path -or $service.PathName -ne $BinPath) { throw 'service binPath is not owned' }
  if ($service.StartName -ne 'NT AUTHORITY\LocalService') { throw 'service account is not exact LocalService' }
  if ($service.Description -ne ('AssetLibrary M0-004 Spike ' + $marker.install_id)) { throw 'service description/install ID is not owned' }
  return $service
}
function Wait-ServiceState([string]$State, [int]$TimeoutSeconds = 15) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  do {
    $service = Get-ServiceObject
    if ($null -eq $service) { return $false }
    if ($service.State -eq $State) { return $true }
    Start-Sleep -Milliseconds 200
  } while ($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
  return $false
}
function Wait-ServiceAbsent([int]$TimeoutSeconds = 15) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  do {
    if ($null -eq (Get-ServiceObject)) { return $true }
    Start-Sleep -Milliseconds 200
  } while ($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
  return $false
}
function Assert-PortAvailable([int]$Port) {
  if (@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).Count -gt 0) { throw "port is already occupied: $Port" }
}
function Invoke-Icacls([string]$Path, [string]$Permission) {
  & icacls.exe $Path /grant ('*S-1-5-19:' + $Permission) /T /C | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "icacls failed for $Path ($LASTEXITCODE)" }
}
function Remove-EmptyOwnedData {
  if (!(Test-Path -LiteralPath $Data -PathType Container)) { return }
  if (@(Get-ChildItem -LiteralPath $Data -Force).Count -eq 0) { Remove-Item -LiteralPath $Data -Force }
}
function Remove-OwnedStaging {
  $marker = Get-Marker
  if ($null -ne (Get-ServiceObject)) { throw 'refusing to remove staging while service exists' }
  if ($marker.bin_path -ne $BinPath -or $marker.data_path -ne $Data) { throw 'refusing to remove unknown staging' }
  Remove-Item -LiteralPath $StageApp -Recurse -Force
  Remove-EmptyOwnedData
  if (!(Test-Path -LiteralPath $Data)) { Remove-Item -LiteralPath $Marker -Force }
  if ((Test-Path -LiteralPath $StageRoot) -and @(Get-ChildItem -LiteralPath $StageRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $StageRoot -Force }
}
function Install-Service {
  $stagingCreated = $false
  $serviceCreated = $false
  try {
    if (!(Test-Path -LiteralPath $SourceExe -PathType Leaf)) { throw "artifact not found: $SourceExe" }
    if ($null -ne (Get-ServiceObject)) { throw "service already exists: $Name" }
    if (Test-Path -LiteralPath $StageRoot) { throw 'staging root already exists; refusing to overwrite' }
    Assert-PortAvailable 5080
    New-Item -ItemType Directory -Path $StageApp -Force | Out-Null
    $stagingCreated = $true
    $installId = [Guid]::NewGuid().ToString('D')
    $description = 'AssetLibrary M0-004 Spike ' + $installId
    $markerValue = [ordered]@{ owner = $Owner; install_id = $installId; bin_path = $BinPath; data_path = $Data }
    $markerValue | ConvertTo-Json -Compress | Set-Content -LiteralPath $Marker -Encoding UTF8 -NoNewline
    Get-ChildItem -LiteralPath $ArtifactDir -Force | Copy-Item -Destination $StageApp -Recurse -Force
    New-Item -ItemType Directory -Path $Data -Force | Out-Null
    Invoke-Icacls $StageApp '(OI)(CI)(RX)'
    Invoke-Icacls $Data '(OI)(CI)(M)'
    Invoke-Sc @('create',$Name,'binPath=',$BinPath,'start=','demand','obj=','NT AUTHORITY\LocalService')
    $serviceCreated = $true
    Invoke-Sc @('description',$Name,$description)
    [void](Assert-OwnedService)
    @{ action='install'; name=$Name; state='Stopped'; binPath=$BinPath; startName='NT AUTHORITY\LocalService'; absent=$false } | ConvertTo-Json -Compress
  } catch {
    $installError = $_
    if ($stagingCreated) {
      if ($serviceCreated -and $null -ne (Get-ServiceObject)) {
        $rollbackService = Get-ServiceObject
        if ($rollbackService.PathName -ne $BinPath -or $rollbackService.StartName -ne 'NT AUTHORITY\LocalService') { throw 'install cleanup incomplete; service ownership readback failed and staging was retained' }
        try { Invoke-Sc @('delete',$Name) } catch { throw 'install cleanup incomplete; service remains and staging was retained' }
        if (!(Wait-ServiceAbsent)) { throw 'install cleanup incomplete; service absence was not confirmed and staging was retained' }
      }
      if ($null -eq (Get-ServiceObject)) { Remove-OwnedStaging }
    }
    throw $installError
  }
}
function Invoke-Health {
  $service = Assert-OwnedService
  if ($service.State -ne 'Running' -or [int]$service.ProcessId -le 0) { throw 'service is not running with a valid process ID' }
  $listener = @(Get-NetTCPConnection -State Listen -LocalPort 5080 -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $service.ProcessId)
  if ($listener.Count -ne 1) { throw 'service process does not own exactly one 5080 listener' }
  & $StageExe --health-probe --spike-port 5080 --spike-probe-host 127.0.0.1 | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'health probe did not confirm m0-004/v1 status ok' }
  @{ action='health'; name=$Name; state=$service.State; binPath=$service.PathName; startName=$service.StartName; processId=[int]$service.ProcessId; absent=$false } | ConvertTo-Json -Compress
}

$mutex = [Threading.Mutex]::new($false, $MutexName)
$mutexHeld = $false
try {
  if (!$mutex.WaitOne(30000)) { throw 'operation mutex timeout' }
  $mutexHeld = $true
  if ($Action -eq 'install') { Install-Service; exit 0 }
  if ($Action -eq 'start') { $service = Assert-OwnedService; Invoke-Sc @('start',$Name); $service = Assert-OwnedService; if (!(Wait-ServiceState 'Running')) { throw 'service did not reach Running state' }; @{ action='start'; name=$Name; state='Running'; binPath=$service.PathName; startName=$service.StartName; processId=[int]$service.ProcessId; absent=$false } | ConvertTo-Json -Compress; exit 0 }
  if ($Action -eq 'stop') { $service = Assert-OwnedService; if ($service.State -ne 'Stopped') { $service = Assert-OwnedService; Invoke-Sc @('stop',$Name); $service = Assert-OwnedService; if (!(Wait-ServiceState 'Stopped')) { throw 'service did not reach Stopped state' } }; @{ action='stop'; name=$Name; state='Stopped'; binPath=$service.PathName; startName=$service.StartName; processId=[int]$service.ProcessId; absent=$false } | ConvertTo-Json -Compress; exit 0 }
  if ($Action -eq 'health') { Invoke-Health; exit 0 }
  if ($Action -eq 'uninstall') {
    $service = Get-ServiceObject
    if ($null -eq $service) {
      if (!(Test-Path -LiteralPath $StageRoot)) { @{ action='uninstall'; name=$Name; absent=$true } | ConvertTo-Json -Compress; exit 0 }
      [void](Get-Marker); Remove-OwnedStaging; @{ action='uninstall'; name=$Name; absent=$true } | ConvertTo-Json -Compress; exit 0
    }
    $service = Assert-OwnedService
    if ($service.State -ne 'Stopped') { $service = Assert-OwnedService; Invoke-Sc @('stop',$Name); $service = Assert-OwnedService; if (!(Wait-ServiceState 'Stopped')) { throw 'service did not stop within timeout' } }
    $service = Assert-OwnedService; Invoke-Sc @('delete',$Name); if (!(Wait-ServiceAbsent)) { throw 'service absence was not confirmed' }
    Remove-OwnedStaging; @{ action='uninstall'; name=$Name; absent=$true } | ConvertTo-Json -Compress; exit 0
  }
} finally {
  if ($mutexHeld) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
