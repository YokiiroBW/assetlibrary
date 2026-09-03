param(
    [ValidateSet('preflight', 'cycle', 'cleanup', 'verify-absent')]
    [string]$Action = 'preflight',
    [string]$EvidenceRoot = '',
    [string]$ArtifactDir = '',
    [string]$ExpectedComputerName = '',
    [string]$ExpectedSourceRevision = '',
    [switch]$ApproveSystemChanges
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'AssetLibrary-V01-008-Evidence'
$OwnerMarker = 'AssetLibrary/V01-008/windows-service-evidence/v1'
$EvidenceMarkerName = '.assetlibrary-v01-008-evidence-root'
$StateMarkerName = '.assetlibrary-v01-008-owned-state'
$Repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$TaskRoot = [IO.Path]::GetFullPath((Join-Path $Repo '.runtime\sandbox-storage\V01-008'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $TaskRoot 'windows-service'
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Join-Path $EvidenceRoot 'artifact\win-x64'
}
$ArtifactDir = [IO.Path]::GetFullPath($ArtifactDir)
$Executable = Join-Path $ArtifactDir 'AssetLibrary.CoreServer.Host.exe'
$StatePath = [IO.Path]::GetFullPath((Join-Path $EvidenceRoot 'state'))
$EvidenceMarker = Join-Path $EvidenceRoot $EvidenceMarkerName
$StateMarker = Join-Path $StatePath $StateMarkerName
$ServiceKey = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$ServiceName"
$ImagePath = '"{0}" --state-path "{1}" --environment Production --bind-host 127.0.0.1 --probe-host 127.0.0.1 --port 5088' -f $Executable, $StatePath
$CreatedThisRun = $false

function Test-Elevated {
    $Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal = [Security.Principal.WindowsPrincipal]$Identity
    return $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-StrictDescendant([string]$Parent, [string]$Child, [string]$Label) {
    $ParentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $ChildFull = [IO.Path]::GetFullPath($Child)
    if (!$ChildFull.StartsWith($ParentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must be a strict descendant of the V01-008 task root"
    }
}

function Assert-NoReparsePoint([string]$Path) {
    $Current = [IO.Path]::GetFullPath($Path)
    while ($Current.StartsWith($TaskRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $Current) {
            $Item = Get-Item -LiteralPath $Current -Force
            if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'reparse points are not allowed in the evidence boundary'
            }
        }
        if ($Current -eq $TaskRoot) { break }
        $Current = [IO.Directory]::GetParent($Current).FullName
    }
}

function Assert-EvidenceBoundary {
    Assert-StrictDescendant $TaskRoot $EvidenceRoot 'EvidenceRoot'
    Assert-StrictDescendant $EvidenceRoot $ArtifactDir 'ArtifactDir'
    Assert-StrictDescendant $EvidenceRoot $StatePath 'StatePath'
    Assert-NoReparsePoint $EvidenceRoot
    Assert-NoReparsePoint $ArtifactDir
    Assert-NoReparsePoint $StatePath
    if (!(Test-Path -LiteralPath $EvidenceMarker -PathType Leaf)) {
        throw "missing evidence marker: $EvidenceMarkerName"
    }
}

function Get-ServiceRegistration {
    if (!(Test-Path -LiteralPath $ServiceKey)) { return $null }
    return Get-ItemProperty -LiteralPath $ServiceKey
}

function Assert-OwnedService([switch]$AllowMissingOwnerMarker) {
    $Registration = Get-ServiceRegistration
    if ($null -eq $Registration) { throw 'evidence service is not installed' }
    if ($Registration.AssetLibraryEvidenceOwner -ne $OwnerMarker -and !($AllowMissingOwnerMarker -and $CreatedThisRun)) {
        throw 'refusing to modify an unowned service registration'
    }
    if ($Registration.ImagePath -ne $ImagePath) {
        throw 'refusing to modify a service with an unexpected ImagePath'
    }
}

function Invoke-ServiceControl([string[]]$Arguments) {
    & sc.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "service control failed with exit code $LASTEXITCODE"
    }
}

function Wait-ServiceState([string]$Expected, [int]$Seconds = 20) {
    $Deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $Service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($Expected -eq 'Absent' -and $null -eq $Service) { return }
        if ($null -ne $Service -and $Service.Status.ToString() -eq $Expected) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "service did not reach the expected state within $Seconds seconds"
}

function Wait-ServiceHealth([int]$Seconds = 20) {
    $Deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        & $Executable --health-probe --probe-host 127.0.0.1 --port 5088
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "service did not become healthy within $Seconds seconds"
}

function Remove-OwnedState {
    if (!(Test-Path -LiteralPath $StatePath)) { return }
    Assert-StrictDescendant $EvidenceRoot $StatePath 'StatePath'
    Assert-NoReparsePoint $StatePath
    if (!(Test-Path -LiteralPath $StateMarker -PathType Leaf)) {
        throw 'refusing to remove a state directory without its owner marker'
    }
    Remove-Item -LiteralPath $StatePath -Recurse -Force
}

function Invoke-OwnedCleanup([switch]$AllowCreatedThisRun) {
    $Registration = Get-ServiceRegistration
    if ($null -ne $Registration) {
        Assert-OwnedService -AllowMissingOwnerMarker:$AllowCreatedThisRun
        $Service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $Service -and $Service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
            Invoke-ServiceControl @('stop', $ServiceName)
            Wait-ServiceState 'Stopped'
        }
        Invoke-ServiceControl @('delete', $ServiceName)
        Wait-ServiceState 'Absent'
        $script:CreatedThisRun = $false
    }
    Remove-OwnedState
}

function Get-Residue {
    $ProcessCount = @(Get-Process -Name 'AssetLibrary.CoreServer.Host' -ErrorAction SilentlyContinue).Count
    $ListenerCount = @(Get-NetTCPConnection -LocalPort 5088 -State Listen -ErrorAction SilentlyContinue).Count
    return [ordered]@{
        service = $null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)
        registry = Test-Path -LiteralPath $ServiceKey
        state = Test-Path -LiteralPath $StatePath
        process_count = $ProcessCount
        listener_count = $ListenerCount
    }
}

if ($Action -eq 'preflight') {
    $BoundaryValid = $false
    try { Assert-EvidenceBoundary; $BoundaryValid = $true } catch { $BoundaryValid = $false }
    [ordered]@{
        contract = 'v01-008/1'
        action = 'preflight'
        elevated = Test-Elevated
        computer_matches = ![string]::IsNullOrWhiteSpace($ExpectedComputerName) -and $env:COMPUTERNAME -eq $ExpectedComputerName
        boundary_valid = $BoundaryValid
        artifact_present = Test-Path -LiteralPath $Executable -PathType Leaf
        source_revision_supplied = $ExpectedSourceRevision -cmatch '^[0-9a-f]{40}$'
        residue = Get-Residue
    } | ConvertTo-Json -Depth 4 -Compress
    exit 0
}

Assert-EvidenceBoundary
if (!$ApproveSystemChanges) { throw 'system changes require -ApproveSystemChanges' }
if ([string]::IsNullOrWhiteSpace($ExpectedComputerName) -or $env:COMPUTERNAME -ne $ExpectedComputerName) {
    throw 'computer name confirmation does not match this host'
}
if (!(Test-Elevated)) { throw 'this action requires an explicitly elevated PowerShell session' }

if ($Action -eq 'verify-absent') {
    $Residue = Get-Residue
    if ($Residue.service -or $Residue.registry -or $Residue.state -or $Residue.process_count -or $Residue.listener_count) {
        throw 'Windows Service evidence residue is present'
    }
    [ordered]@{ contract = 'v01-008/1'; action = 'verify-absent'; residue = $Residue } |
        ConvertTo-Json -Depth 4 -Compress
    exit 0
}

if ($Action -eq 'cleanup') {
    Invoke-OwnedCleanup
    & $PSCommandPath -Action verify-absent -EvidenceRoot $EvidenceRoot -ArtifactDir $ArtifactDir `
        -ExpectedComputerName $ExpectedComputerName -ApproveSystemChanges
    exit $LASTEXITCODE
}

if (!(Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'win-x64 host artifact is missing' }
if ($ExpectedSourceRevision -cnotmatch '^[0-9a-f]{40}$') {
    throw 'cycle requires a full lowercase -ExpectedSourceRevision'
}
$BuildInfoText = & $Executable --build-info
if ($LASTEXITCODE -ne 0) { throw 'artifact build-info failed' }
$BuildInfo = $BuildInfoText | ConvertFrom-Json
if ($BuildInfo.contract -ne 'v01-008/1' -or $BuildInfo.source_revision -ne $ExpectedSourceRevision) {
    throw 'artifact build-info does not match the expected release provenance'
}
$Before = Get-Residue
if ($Before.service -or $Before.registry -or $Before.state -or $Before.process_count -or $Before.listener_count) {
    throw 'pre-existing service evidence state must be cleaned before the cycle'
}

$StartedAt = [DateTime]::UtcNow
$CyclePassed = $false
try {
    New-Item -ItemType Directory -Path $StatePath | Out-Null
    New-Item -ItemType File -Path $StateMarker | Out-Null
    & icacls.exe $StatePath /grant '*S-1-5-19:(OI)(CI)M' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'failed to grant LocalService access to the evidence state path' }

    Invoke-ServiceControl @('create', $ServiceName, 'binPath=', $ImagePath, 'start=', 'demand', 'obj=', 'NT AUTHORITY\LocalService')
    $CreatedThisRun = $true
    New-ItemProperty -LiteralPath $ServiceKey -Name AssetLibraryEvidenceOwner `
        -PropertyType String -Value $OwnerMarker -Force | Out-Null
    Invoke-ServiceControl @('description', $ServiceName, 'AssetLibrary V01-008 isolated release evidence')
    Assert-OwnedService
    Invoke-ServiceControl @('start', $ServiceName)
    Wait-ServiceState 'Running'
    Wait-ServiceHealth
    $Registration = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ($Registration.StartName -ne 'NT AUTHORITY\LocalService') { throw 'service identity is not LocalService' }
    $CyclePassed = $true
} finally {
    Invoke-OwnedCleanup -AllowCreatedThisRun
}

$After = Get-Residue
if (!$CyclePassed -or $After.service -or $After.registry -or $After.state -or $After.process_count -or $After.listener_count) {
    throw 'Windows Service cycle failed or left residue'
}
[ordered]@{
    contract = 'v01-008/1'
    status = 'passed'
    target = 'windows_service'
    source_revision = $ExpectedSourceRevision
    identity = 'LocalService'
    elapsed_ms = [int]([DateTime]::UtcNow - $StartedAt).TotalMilliseconds
    residue = $After
} | ConvertTo-Json -Depth 4 -Compress
