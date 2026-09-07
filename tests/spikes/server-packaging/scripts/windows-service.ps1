param(
    [ValidateSet('preflight', 'install', 'start', 'health', 'stop', 'uninstall', 'verify-absent')]
    [string]$Action = 'preflight',
    [string]$ArtifactDir = ''
)

$ErrorActionPreference = 'Stop'
$Name = 'AssetLibrary-M0-004-Spike'
$OwnerMarker = 'AssetLibrary/M0-004/v1'
$Repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Join-Path $Repo '.runtime\sandbox-storage\M0-004\artifact\win-x64'
}
$ArtifactDir = [IO.Path]::GetFullPath($ArtifactDir)
$SourceExe = Join-Path $ArtifactDir 'ServerPackagingSpike.exe'
$StageParent = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'Temp'))
$StageRoot = Join-Path $StageParent $Name
$StageApp = Join-Path $StageRoot 'app'
$Data = Join-Path $StageRoot 'data'
$StageExe = Join-Path $StageApp 'ServerPackagingSpike.exe'
$Marker = Join-Path $StageRoot 'owner.json'
$LegacyData = Join-Path $Repo '.runtime\sandbox-storage\M0-004\service-data'
$ServiceKey = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$Name"
$ImagePath = '"{0}" --SPIKE_DATA_PATH "{1}" --SPIKE_PORT 5080 --SPIKE_BIND_HOST 127.0.0.1' -f $StageExe, $Data

function Test-Elevated {
    $Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal = [Security.Principal.WindowsPrincipal]$Identity
    return $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Elevated {
    if (!(Test-Elevated)) {
        throw "$Action requires an explicitly approved elevated PowerShell session"
    }
}

function Invoke-Sc([string[]]$Arguments) {
    # $Args is PowerShell's automatic unbound-arguments variable, not a safe parameter name.
    & sc.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }
}

function Get-ServiceObject {
    # A failed query is not evidence that it is safe to delete staged files.
    Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -OperationTimeoutSec 5 -ErrorAction Stop
}

function Get-ServiceRegistration {
    if (!(Test-Path -LiteralPath $ServiceKey)) { return $null }
    Get-ItemProperty -LiteralPath $ServiceKey -ErrorAction Stop
}

function Assert-StagingPaths {
    $ExpectedRoot = [IO.Path]::GetFullPath((Join-Path $StageParent $Name))
    if ([IO.Path]::GetFullPath($StageRoot) -ne $ExpectedRoot) { throw 'unexpected staging root' }
    $Expected = @{
        $StageApp = (Join-Path $ExpectedRoot 'app')
        $Data = (Join-Path $ExpectedRoot 'data')
        $StageExe = (Join-Path $ExpectedRoot 'app\ServerPackagingSpike.exe')
        $Marker = (Join-Path $ExpectedRoot 'owner.json')
    }
    foreach ($Path in $Expected.Keys) {
        if ([IO.Path]::GetFullPath($Path) -ne [IO.Path]::GetFullPath($Expected[$Path])) {
            throw 'staging path escapes its owned location'
        }
    }
    foreach ($Path in @($StageRoot, $StageApp, $Data, $Marker)) {
        if (Test-Path -LiteralPath $Path) {
            $Item = Get-Item -LiteralPath $Path -Force
            if ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'staging reparse points are not owned' }
        }
    }
}

function Get-Marker {
    Assert-StagingPaths
    if (!(Test-Path -LiteralPath $Marker -PathType Leaf)) { throw 'owner marker is missing' }
    $Value = Get-Content -LiteralPath $Marker -Raw | ConvertFrom-Json
    $Parsed = [Guid]::Empty
    if ($Value.owner -ne $OwnerMarker -or ![Guid]::TryParse([string]$Value.install_id, [ref]$Parsed)) {
        throw 'owner marker is invalid'
    }
    if ($Value.bin_path -ne $ImagePath -or $Value.data_path -ne $Data) { throw 'owner marker paths do not match canonical staging' }
    return $Value
}

function Assert-OwnedService {
    $Service = Get-ServiceObject
    if ($null -eq $Service) { throw "service is not installed: $Name" }
    $Owned = Get-Marker
    $Registration = Get-ServiceRegistration
    if ($null -eq $Registration -or $Registration.AssetLibrarySpikeOwner -ne $OwnerMarker -or $Registration.ImagePath -ne $ImagePath) {
        throw 'service registry ownership does not match'
    }
    if ($Service.PathName -ne $ImagePath) { throw 'service binPath is not owned' }
    if ($Service.StartName -ne 'NT AUTHORITY\LocalService') { throw 'service account is not exact LocalService' }
    if ($Service.Description -ne ('AssetLibrary M0-004 Spike ' + $Owned.install_id)) { throw 'service description/install ID is not owned' }
    return $Service
}

function Wait-ServiceState([string]$State, [int]$Seconds = 15) {
    $Watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $Service = Get-ServiceObject
        if ($null -ne $Service -and $Service.State -eq $State) { return }
        Start-Sleep -Milliseconds 200
    } while ($Watch.Elapsed.TotalSeconds -lt $Seconds)
    throw "service did not reach state '$State' within $Seconds seconds"
}

function Wait-ServiceAbsent([int]$Seconds = 15) {
    $Watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($null -eq (Get-ServiceObject) -and $null -eq (Get-ServiceRegistration)) { return }
        Start-Sleep -Milliseconds 200
    } while ($Watch.Elapsed.TotalSeconds -lt $Seconds)
    throw 'service absence was not confirmed'
}

function Assert-PortAvailable {
    $Listeners = @(Get-NetTCPConnection -ErrorAction Stop | Where-Object { $_.State -eq 'Listen' -and $_.LocalPort -eq 5080 })
    if ($Listeners.Count -gt 0) { throw 'port is already occupied: 5080' }
}

function Invoke-Icacls([string]$Path, [string]$Permission) {
    & icacls.exe $Path /grant ('*S-1-5-19:' + $Permission) /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed ($LASTEXITCODE)" }
}

function Remove-OwnedStaging {
    [void](Get-Marker)
    if ($null -ne (Get-ServiceObject) -or $null -ne (Get-ServiceRegistration)) { throw 'refusing to remove staging while service exists' }
    $Top = @(Get-ChildItem -LiteralPath $StageRoot -Force)
    if ($Top.Name | Where-Object { $_ -notin @('app', 'data', 'owner.json') }) { throw 'refusing to remove unknown staging item' }
    if ((Test-Path -LiteralPath $Data) -and !(Test-Path -LiteralPath $Data -PathType Container)) { throw 'staging data is not a directory' }
    if (Test-Path -LiteralPath $StageApp) {
        if (!(Test-Path -LiteralPath $StageApp -PathType Container)) { throw 'staging app is not a directory' }
        if (Get-ChildItem -LiteralPath $StageApp -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
            throw 'staging app contains an unowned reparse point'
        }
        Remove-Item -LiteralPath $StageApp -Recurse -Force
    }
    # Persisted evidence/data is never recursively removed by the service adapter.
    if ((Test-Path -LiteralPath $Data) -and @(Get-ChildItem -LiteralPath $Data -Force).Count -eq 0) {
        Remove-Item -LiteralPath $Data -Force
    }
    if (!(Test-Path -LiteralPath $Data)) { Remove-Item -LiteralPath $Marker -Force }
    if (@(Get-ChildItem -LiteralPath $StageRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $StageRoot -Force }
}

function Write-ServiceResult($Service) {
    @{ action = $Action; name = $Name; state = $Service.State; binPath = $Service.PathName; startName = $Service.StartName; processId = [int]$Service.ProcessId; absent = $false } | ConvertTo-Json -Compress
}

function Install-Service {
    $StagingCreated = $false
    $ServiceCreated = $false
    try {
        Assert-StagingPaths
        if (!(Test-Path -LiteralPath $SourceExe -PathType Leaf)) { throw "artifact not found: $SourceExe" }
        if ($null -ne (Get-ServiceObject) -or $null -ne (Get-ServiceRegistration)) { throw "service already exists: $Name" }
        if ((Test-Path -LiteralPath $StageRoot) -or (Test-Path -LiteralPath $LegacyData)) { throw 'staging or legacy data already exists; refusing to overwrite' }
        if (Get-ChildItem -LiteralPath $ArtifactDir -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
            throw 'artifact contains a reparse point'
        }
        Assert-PortAvailable
        New-Item -ItemType Directory -Path $StageRoot | Out-Null
        $StagingCreated = $true
        $InstallId = [Guid]::NewGuid().ToString('D')
        [ordered]@{ owner = $OwnerMarker; install_id = $InstallId; bin_path = $ImagePath; data_path = $Data } |
            ConvertTo-Json -Compress | Set-Content -LiteralPath $Marker -Encoding UTF8 -NoNewline
        New-Item -ItemType Directory -Path $StageApp, $Data | Out-Null
        Get-ChildItem -LiteralPath $ArtifactDir -Force | Copy-Item -Destination $StageApp -Recurse -Force
        Invoke-Icacls $StageRoot '(OI)(CI)(RX)'
        Invoke-Icacls $StageApp '(OI)(CI)(RX)'
        Invoke-Icacls $Data '(OI)(CI)(M)'
        Invoke-Sc @('create', $Name, 'binPath=', $ImagePath, 'start=', 'demand', 'obj=', 'NT AUTHORITY\LocalService')
        $ServiceCreated = $true
        New-ItemProperty -LiteralPath $ServiceKey -Name AssetLibrarySpikeOwner -PropertyType String -Value $OwnerMarker | Out-Null
        Invoke-Sc @('description', $Name, ('AssetLibrary M0-004 Spike ' + $InstallId))
        $Service = Assert-OwnedService
        if ($Service.State -ne 'Stopped') { throw 'service did not remain Stopped after install' }
        Write-ServiceResult $Service
    } catch {
        $InstallError = $_
        if ($StagingCreated) {
            try {
                if ($ServiceCreated) {
                    $Rollback = Get-ServiceObject
                    if ($null -ne $Rollback) {
                        if ($Rollback.PathName -ne $ImagePath -or $Rollback.StartName -ne 'NT AUTHORITY\LocalService') {
                            throw 'service ownership readback failed'
                        }
                        Invoke-Sc @('delete', $Name)
                    }
                    Wait-ServiceAbsent
                }
                Remove-OwnedStaging
            } catch {
                throw "install cleanup incomplete; staging was retained: $($_.Exception.Message). Install failure: $($InstallError.Exception.Message)"
            }
        }
        throw $InstallError
    }
}

function Invoke-Health {
    $Service = Assert-OwnedService
    if ($Service.State -ne 'Running' -or [int]$Service.ProcessId -le 0) { throw 'service is not running with a valid process ID' }
    $Listeners = @(Get-NetTCPConnection -ErrorAction Stop | Where-Object { $_.State -eq 'Listen' -and $_.LocalPort -eq 5080 -and $_.OwningProcess -eq $Service.ProcessId -and $_.LocalAddress -eq '127.0.0.1' })
    if ($Listeners.Count -ne 1) { throw 'service process does not own exactly one loopback 5080 listener' }
    & $StageExe --health-probe --SPIKE_PORT 5080 --SPIKE_PROBE_HOST 127.0.0.1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'health probe did not confirm m0-004/v1 status ok' }
    Write-ServiceResult $Service
}

if ($Action -eq 'preflight') {
    $Service = Get-ServiceObject
    $Registration = Get-ServiceRegistration
    [pscustomobject]@{
        service_name = $Name
        executable_present = Test-Path -LiteralPath $SourceExe -PathType Leaf
        service_present = $null -ne $Service -or $null -ne $Registration
        owned_registration = $null -ne $Registration -and $Registration.AssetLibrarySpikeOwner -eq $OwnerMarker
        data_path_present = (Test-Path -LiteralPath $Data) -or (Test-Path -LiteralPath $LegacyData)
        staging_path_present = Test-Path -LiteralPath $StageRoot
        elevated = Test-Elevated
    } | ConvertTo-Json -Compress
    exit 0
}

if ($Action -eq 'verify-absent') {
    $ServicePresent = $null -ne (Get-ServiceObject)
    $RegistryPresent = $null -ne (Get-ServiceRegistration)
    $DataPresent = (Test-Path -LiteralPath $StageRoot) -or (Test-Path -LiteralPath $LegacyData)
    if ($ServicePresent -or $RegistryPresent -or $DataPresent) {
        throw "residue detected: service=$ServicePresent registry=$RegistryPresent data=$DataPresent"
    }
    Write-Output '{"service":false,"registry":false,"data":false}'
    exit 0
}

if ($Action -ne 'health') { Assert-Elevated }
$Mutex = [Threading.Mutex]::new($false, 'Global\AssetLibrary-M0-004-Spike')
$MutexHeld = $false
try {
    try { $MutexHeld = $Mutex.WaitOne(30000) }
    catch [Threading.AbandonedMutexException] { $MutexHeld = $true }
    if (!$MutexHeld) { throw 'operation mutex timeout' }
    if ($Action -eq 'install') { Install-Service; exit 0 }
    if ($Action -eq 'health') { Invoke-Health; exit 0 }
    if ($Action -eq 'uninstall' -and $null -eq (Get-ServiceObject) -and $null -eq (Get-ServiceRegistration)) {
        if (Test-Path -LiteralPath $StageRoot) { Remove-OwnedStaging }
    } else {
        $Service = Assert-OwnedService
        if ($Action -eq 'start') {
            if ($Service.State -ne 'Running') { Invoke-Sc @('start', $Name); Wait-ServiceState 'Running' }
            $Service = Assert-OwnedService
            if ($Service.State -ne 'Running') { throw 'service readback is not Running' }
            Write-ServiceResult $Service
            exit 0
        }
        if ($Service.State -ne 'Stopped') { Invoke-Sc @('stop', $Name); Wait-ServiceState 'Stopped' }
        $Service = Assert-OwnedService
        if ($Service.State -ne 'Stopped') { throw 'service readback is not Stopped' }
        if ($Action -eq 'stop') { Write-ServiceResult $Service; exit 0 }
        Invoke-Sc @('delete', $Name)
        Wait-ServiceAbsent
        Remove-OwnedStaging
    }
    @{ action = 'uninstall'; name = $Name; absent = $true; retained_data = (Test-Path -LiteralPath $Data) -or (Test-Path -LiteralPath $LegacyData) } | ConvertTo-Json -Compress
} finally {
    if ($MutexHeld) { $Mutex.ReleaseMutex() }
    $Mutex.Dispose()
}
