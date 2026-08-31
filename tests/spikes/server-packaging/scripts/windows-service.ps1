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
$Exe = Join-Path $ArtifactDir 'ServerPackagingSpike.exe'
$Data = Join-Path $Repo '.runtime\sandbox-storage\M0-004\service-data'
$ServiceKey = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$Name"
$ImagePath = '"{0}" --SPIKE_DATA_PATH "{1}" --SPIKE_PORT 5080 --SPIKE_BIND_HOST 127.0.0.1' -f $Exe, $Data

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
    & sc.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }
}

function Get-ServiceRegistration {
    if (!(Test-Path -LiteralPath $ServiceKey)) { return $null }
    return Get-ItemProperty -LiteralPath $ServiceKey
}

function Assert-OwnedService {
    $Registration = Get-ServiceRegistration
    if ($null -eq $Registration) { throw "service is not installed: $Name" }
    if ($Registration.AssetLibrarySpikeOwner -ne $OwnerMarker) {
        throw "refusing to modify unowned service registration: $Name"
    }
    if ($Registration.ImagePath -ne $ImagePath) {
        throw "refusing service registration with an unexpected ImagePath: $Name"
    }
}

function Wait-ServiceState([string]$State, [int]$Seconds = 15) {
    $Deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $Service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $Service) {
            if ($State -eq 'Absent') { return }
        } elseif ($Service.Status.ToString() -eq $State) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "service did not reach state '$State' within $Seconds seconds"
}

if ($Action -eq 'preflight') {
    $Registration = Get-ServiceRegistration
    [pscustomobject]@{
        service_name = $Name
        executable_present = Test-Path -LiteralPath $Exe
        service_present = $null -ne $Registration
        owned_registration = $null -ne $Registration -and $Registration.AssetLibrarySpikeOwner -eq $OwnerMarker
        data_path_present = Test-Path -LiteralPath $Data
        elevated = Test-Elevated
    } | ConvertTo-Json -Compress
    exit 0
}

if ($Action -eq 'verify-absent') {
    $ServicePresent = $null -ne (Get-Service -Name $Name -ErrorAction SilentlyContinue)
    $RegistryPresent = Test-Path -LiteralPath $ServiceKey
    $DataPresent = Test-Path -LiteralPath $Data
    if ($ServicePresent -or $RegistryPresent -or $DataPresent) {
        throw "residue detected: service=$ServicePresent registry=$RegistryPresent data=$DataPresent"
    }
    Write-Output '{"service":false,"registry":false,"data":false}'
    exit 0
}

if ($Action -eq 'health') {
    if (!(Test-Path -LiteralPath $Exe)) { throw "artifact not found: $Exe" }
    $env:SPIKE_PORT = '5080'
    $env:SPIKE_PROBE_HOST = '127.0.0.1'
    & $Exe --health-probe
    exit $LASTEXITCODE
}

Assert-Elevated

if ($Action -eq 'install') {
    if (!(Test-Path -LiteralPath $Exe)) { throw "artifact not found: $Exe" }
    if ($null -ne (Get-ServiceRegistration)) { throw "service name already exists: $Name" }
    if (Test-Path -LiteralPath $Data) { throw "refusing pre-existing service data path: $Data" }
    New-Item -ItemType Directory -Path $Data | Out-Null
    & icacls.exe $Data /grant '*S-1-5-19:(OI)(CI)M'
    if ($LASTEXITCODE -ne 0) {
        [IO.Directory]::Delete($Data, $true)
        throw "failed to grant LocalService access to the isolated data path ($LASTEXITCODE)"
    }
    $Created = $false
    try {
        Invoke-Sc @('create', $Name, 'binPath=', $ImagePath, 'start=', 'demand', 'obj=', 'NT AUTHORITY\LocalService')
        $Created = $true
        New-ItemProperty -LiteralPath $ServiceKey -Name AssetLibrarySpikeOwner -PropertyType String -Value $OwnerMarker -Force | Out-Null
        Invoke-Sc @('description', $Name, 'AssetLibrary M0-004 test-only packaging spike')
        Assert-OwnedService
    } catch {
        if ($Created) { & sc.exe delete $Name | Out-Null }
        if (Test-Path -LiteralPath $Data) { [IO.Directory]::Delete($Data, $true) }
        throw
    }
    exit 0
}

Assert-OwnedService

if ($Action -eq 'start') {
    Invoke-Sc @('start', $Name)
    Wait-ServiceState 'Running'
    exit 0
}

if ($Action -eq 'stop') {
    $Service = Get-Service -Name $Name
    if ($Service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Invoke-Sc @('stop', $Name)
        Wait-ServiceState 'Stopped'
    }
    exit 0
}

if ($Action -eq 'uninstall') {
    $Service = Get-Service -Name $Name
    if ($Service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Invoke-Sc @('stop', $Name)
        Wait-ServiceState 'Stopped'
    }
    Invoke-Sc @('delete', $Name)
    Wait-ServiceState 'Absent'
    if (Test-Path -LiteralPath $Data) { [IO.Directory]::Delete($Data, $true) }
    & $PSCommandPath -Action verify-absent -ArtifactDir $ArtifactDir
    if ($LASTEXITCODE -ne 0) { throw "post-uninstall residue check failed ($LASTEXITCODE)" }
    exit 0
}
