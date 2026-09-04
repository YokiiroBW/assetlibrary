param(
    [ValidateSet('preflight', 'cycle', 'cleanup', 'verify-absent')]
    [string]$Action = 'preflight',
    [string]$EvidenceRoot = '',
    [string]$ArtifactDir = '',
    [string]$ExpectedComputerName = '',
    [string]$ExpectedSourceRevision = '',
    [string]$ExpectedArtifactTreeSha256 = '',
    [string]$ExpectedRuntimeEvidenceBindingSha256 = '',
    [switch]$ApproveSystemChanges
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
if ($null -eq ('AssetLibrary.V01008.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace AssetLibrary.V01008
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    public static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateDirectory(
            string path,
            ref SecurityAttributes securityAttributes);
    }
}
'@
}
$ServiceName = 'AssetLibrary-V01-008-Evidence'
$OwnerMarker = 'AssetLibrary/V01-008/windows-service-evidence/v1'
$EvidenceMarkerName = '.assetlibrary-v01-008-evidence-root'
$StateMarkerName = '.assetlibrary-v01-008-owned-state'
$SecureMarkerName = '.assetlibrary-v01-008-secure-staging'
$SecureMarkerValue = 'AssetLibrary/V01-008/windows-secure-staging/v1'
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
$SourceExecutable = Join-Path $ArtifactDir 'AssetLibrary.CoreServer.Host.exe'
$SystemTempRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'Temp'))
$SecureRoot = [IO.Path]::GetFullPath((Join-Path $SystemTempRoot 'AssetLibrary-V01-008-Evidence'))
$SecureArtifactDir = Join-Path $SecureRoot 'artifact\win-x64'
$Executable = Join-Path $SecureArtifactDir 'AssetLibrary.CoreServer.Host.exe'
$StatePath = Join-Path $SecureRoot 'state'
$EvidenceMarker = Join-Path $EvidenceRoot $EvidenceMarkerName
$StateMarker = Join-Path $StatePath $StateMarkerName
$SecureMarker = Join-Path $SecureRoot $SecureMarkerName
$ServiceKey = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$ServiceName"
$ImagePath = '"{0}" --state-path "{1}" --environment Production --bind-host 127.0.0.1 --probe-host 127.0.0.1 --port 5088' -f $Executable, $StatePath
$CreatedThisRun = $false
$SecureRootCreatedThisRun = $false

function Test-Elevated {
    $Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal = [Security.Principal.WindowsPrincipal]$Identity
    return $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-StrictDescendant([string]$Parent, [string]$Child, [string]$Label) {
    $ParentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $ChildFull = [IO.Path]::GetFullPath($Child)
    if (!$ChildFull.StartsWith($ParentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must be a strict descendant of its expected parent"
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
    Assert-StrictDescendant $SystemTempRoot $SecureRoot 'SecureRoot'
    Assert-NoReparsePoint $EvidenceRoot
    Assert-NoReparsePoint $ArtifactDir
    Assert-NoReparsePoint $SourceExecutable
    if (!(Test-Path -LiteralPath $EvidenceMarker -PathType Leaf)) {
        throw "missing evidence marker: $EvidenceMarkerName"
    }
}

function Get-ArtifactTreeSha256([string]$Root) {
    if (!(Test-Path -LiteralPath $Root -PathType Container)) {
        throw 'win-x64 artifact directory is missing'
    }
    $Root = [IO.Path]::GetFullPath($Root)
    $RootItem = Get-Item -LiteralPath $Root -Force
    if (($RootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'artifact root may not be a reparse point'
    }
    $RootPrefix = $Root.TrimEnd('\') + '\'
    $Utf8 = [Text.UTF8Encoding]::new($false)
    $Records = [Collections.Generic.SortedDictionary[string,string]]::new(
        [StringComparer]::Ordinal
    )
    foreach ($Item in @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) {
        if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'artifact tree may not contain reparse points'
        }
        if ($Item.PSIsContainer) { continue }
        $Relative = $Item.FullName.Substring($RootPrefix.Length).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative -match '[\x00-\x1f\x7f]') {
            throw 'artifact tree contains an unsafe relative path'
        }
        $Digest = (Get-FileHash -LiteralPath $Item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $SortKey = ([BitConverter]::ToString($Utf8.GetBytes($Relative))).Replace('-', '')
        $Records.Add($SortKey, "$Digest`t$($Item.Length)`t$Relative`n")
    }
    $Text = [Text.StringBuilder]::new()
    foreach ($Record in $Records.Values) { [void]$Text.Append($Record) }
    $Hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $Bytes = $Utf8.GetBytes($Text.ToString())
        return ([BitConverter]::ToString($Hasher.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $Hasher.Dispose()
    }
}

function Get-RuntimeEvidenceBindingSha256 {
    $Text = "AssetLibrary/V01-008/runtime-evidence-binding/v1`n$ExpectedSourceRevision`nwin-x64`n$ExpectedArtifactTreeSha256`n"
    $Hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $Utf8 = [Text.UTF8Encoding]::new($false)
        return ([BitConverter]::ToString($Hasher.ComputeHash($Utf8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant()
    } finally {
        $Hasher.Dispose()
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
        $Handler = [Net.Http.HttpClientHandler]::new()
        $Handler.AllowAutoRedirect = $false
        $Handler.UseProxy = $false
        $Client = [Net.Http.HttpClient]::new($Handler)
        $Client.Timeout = [TimeSpan]::FromSeconds(2)
        $Client.MaxResponseContentBufferSize = 4096
        $Response = $null
        try {
            $Response = $Client.GetAsync('http://127.0.0.1:5088/healthz').GetAwaiter().GetResult()
            if ([int]$Response.StatusCode -eq 200) {
                $Payload = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult() |
                    ConvertFrom-Json
                $Names = @($Payload.PSObject.Properties.Name)
                if (
                    $Names.Count -eq 2 -and
                    $Names -ccontains 'status' -and
                    $Names -ccontains 'contract' -and
                    $Payload.status -ceq 'ok' -and
                    $Payload.contract -ceq 'v01-008/1'
                ) { return }
            }
        } catch {
            # The bounded retry loop reports one stable error after the deadline.
        } finally {
            if ($null -ne $Response) { $Response.Dispose() }
            $Client.Dispose()
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "service did not become healthy within $Seconds seconds"
}

function Remove-OwnedState {
    if (!(Test-Path -LiteralPath $StatePath)) { return }
    Assert-StrictDescendant $SecureRoot $StatePath 'StatePath'
    $StateItem = Get-Item -LiteralPath $StatePath -Force
    if (($StateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'refusing a reparse-point state directory'
    }
    if (!(Test-Path -LiteralPath $StateMarker -PathType Leaf)) {
        throw 'refusing to remove a state directory without its owner marker'
    }
    Remove-Item -LiteralPath $StatePath -Recurse -Force
}

function New-ProtectedDirectory([string]$Path) {
    $Security = [Security.AccessControl.DirectorySecurity]::new()
    $Security.SetAccessRuleProtection($true, $false)
    $Inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $Propagation = [Security.AccessControl.PropagationFlags]::None
    $Allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($Rule in @(
        [Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
            [Security.AccessControl.FileSystemRights]::FullControl,
            $Inheritance,
            $Propagation,
            $Allow
        ),
        [Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'),
            [Security.AccessControl.FileSystemRights]::FullControl,
            $Inheritance,
            $Propagation,
            $Allow
        ),
        [Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new('S-1-5-19'),
            [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            $Inheritance,
            $Propagation,
            $Allow
        )
    )) {
        [void]$Security.AddAccessRule($Rule)
    }
    $Security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $Bytes = [byte[]]::new($Security.BinaryLength)
    $Security.GetSecurityDescriptorBinaryForm($Bytes, 0)
    $Pointer = [Runtime.InteropServices.Marshal]::AllocHGlobal($Bytes.Length)
    try {
        [Runtime.InteropServices.Marshal]::Copy($Bytes, 0, $Pointer, $Bytes.Length)
        $Attributes = [AssetLibrary.V01008.SecurityAttributes]::new()
        $Attributes.Length = [Runtime.InteropServices.Marshal]::SizeOf(
            [type][AssetLibrary.V01008.SecurityAttributes]
        )
        $Attributes.SecurityDescriptor = $Pointer
        $Attributes.InheritHandle = $false
        if (![AssetLibrary.V01008.NativeMethods]::CreateDirectory($Path, [ref]$Attributes)) {
            $ErrorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "secure staging directory creation failed with Win32 error $ErrorCode"
        }
    } finally {
        [Runtime.InteropServices.Marshal]::FreeHGlobal($Pointer)
    }
}

function New-SecureStaging {
    if (Test-Path -LiteralPath $SecureRoot) {
        throw 'secure staging already exists; run owned cleanup first'
    }
    New-ProtectedDirectory $SecureRoot
    $script:SecureRootCreatedThisRun = $true
    $Utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($SecureMarker, $SecureMarkerValue, $Utf8)
    New-Item -ItemType Directory -Path (Split-Path $SecureArtifactDir -Parent) | Out-Null
    Copy-Item -LiteralPath $ArtifactDir -Destination $SecureArtifactDir -Recurse
    if (!(Test-Path -LiteralPath $Executable -PathType Leaf)) {
        throw 'secure staging did not contain the expected executable'
    }
    New-Item -ItemType Directory -Path $StatePath | Out-Null
    New-Item -ItemType File -Path $StateMarker | Out-Null
    & icacls.exe $SecureRoot /inheritance:r /grant:r `
        '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)RX' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'failed to freeze secure staging permissions' }
    & icacls.exe $SecureRoot /setowner '*S-1-5-32-544' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'failed to freeze secure staging ownership' }
    & icacls.exe $StatePath /grant:r '*S-1-5-19:(OI)(CI)M' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'failed to grant LocalService access to secure state' }
    if ((Get-ArtifactTreeSha256 $SecureArtifactDir) -cne $ExpectedArtifactTreeSha256) {
        throw 'secure artifact copy does not match the independently supplied SHA-256'
    }
}

function Remove-OwnedSecureStaging([switch]$AllowCreatedThisRun) {
    if (!(Test-Path -LiteralPath $SecureRoot)) { return }
    $SecureItem = Get-Item -LiteralPath $SecureRoot -Force
    if (($SecureItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'refusing to remove a reparse-point secure staging root'
    }
    $MarkerValid = (Test-Path -LiteralPath $SecureMarker -PathType Leaf) -and
        ([IO.File]::ReadAllText($SecureMarker) -ceq $SecureMarkerValue)
    if (!$MarkerValid -and !($AllowCreatedThisRun -and $SecureRootCreatedThisRun)) {
        throw 'refusing to remove secure staging without its owner marker'
    }
    Remove-Item -LiteralPath $SecureRoot -Recurse -Force
    $script:SecureRootCreatedThisRun = $false
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
    Remove-OwnedSecureStaging -AllowCreatedThisRun:$AllowCreatedThisRun
}

function Get-Residue {
    $ProcessCount = @(Get-Process -Name 'AssetLibrary.CoreServer.Host' -ErrorAction SilentlyContinue).Count
    $ListenerCount = @(Get-NetTCPConnection -LocalPort 5088 -State Listen -ErrorAction SilentlyContinue).Count
    return [ordered]@{
        service = $null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)
        registry = Test-Path -LiteralPath $ServiceKey
        state = Test-Path -LiteralPath $StatePath
        secure_staging = Test-Path -LiteralPath $SecureRoot
        process_count = $ProcessCount
        listener_count = $ListenerCount
    }
}

if ($Action -eq 'preflight') {
    $BoundaryValid = $false
    try { Assert-EvidenceBoundary; $BoundaryValid = $true } catch { $BoundaryValid = $false }
    $ArtifactPresent = $false
    $ArtifactTreeSha256Matches = $false
    if ($BoundaryValid) {
        $ArtifactPresent = Test-Path -LiteralPath $SourceExecutable -PathType Leaf
        if ($ArtifactPresent -and $ExpectedArtifactTreeSha256 -cmatch '^[0-9a-f]{64}$') {
            try {
                $ArtifactTreeSha256Matches = (Get-ArtifactTreeSha256 $ArtifactDir) -ceq $ExpectedArtifactTreeSha256
            } catch {
                $ArtifactTreeSha256Matches = $false
            }
        }
    }
    [ordered]@{
        contract = 'v01-008/1'
        action = 'preflight'
        elevated = Test-Elevated
        computer_matches = ![string]::IsNullOrWhiteSpace($ExpectedComputerName) -and $env:COMPUTERNAME -eq $ExpectedComputerName
        boundary_valid = $BoundaryValid
        artifact_present = $ArtifactPresent
        source_revision_supplied = $ExpectedSourceRevision -cmatch '^[0-9a-f]{40}$'
        artifact_tree_sha256_supplied = $ExpectedArtifactTreeSha256 -cmatch '^[0-9a-f]{64}$'
        artifact_tree_sha256_matches = $ArtifactTreeSha256Matches
        runtime_evidence_binding_supplied = $ExpectedRuntimeEvidenceBindingSha256 -cmatch '^[0-9a-f]{64}$'
        runtime_evidence_binding_matches = (
            $ExpectedSourceRevision -cmatch '^[0-9a-f]{40}$' -and
            $ExpectedArtifactTreeSha256 -cmatch '^[0-9a-f]{64}$' -and
            $ExpectedRuntimeEvidenceBindingSha256 -ceq (Get-RuntimeEvidenceBindingSha256)
        )
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
    if ($Residue.service -or $Residue.registry -or $Residue.state -or $Residue.secure_staging -or $Residue.process_count -or $Residue.listener_count) {
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

if (!(Test-Path -LiteralPath $SourceExecutable -PathType Leaf)) { throw 'win-x64 host artifact is missing' }
if ($ExpectedSourceRevision -cnotmatch '^[0-9a-f]{40}$') {
    throw 'cycle requires a full lowercase -ExpectedSourceRevision'
}
if ($ExpectedArtifactTreeSha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'cycle requires a full lowercase -ExpectedArtifactTreeSha256'
}
if ($ExpectedRuntimeEvidenceBindingSha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'cycle requires a full lowercase -ExpectedRuntimeEvidenceBindingSha256'
}
if ((Get-RuntimeEvidenceBindingSha256) -cne $ExpectedRuntimeEvidenceBindingSha256) {
    throw 'source revision and artifact tree digest do not match the trusted runtime evidence binding'
}
$ArtifactTreeSha256 = Get-ArtifactTreeSha256 $ArtifactDir
if ($ArtifactTreeSha256 -cne $ExpectedArtifactTreeSha256) {
    throw 'artifact tree does not match the independently supplied SHA-256'
}
$Before = Get-Residue
if ($Before.service -or $Before.registry -or $Before.state -or $Before.secure_staging -or $Before.process_count -or $Before.listener_count) {
    throw 'pre-existing service evidence state must be cleaned before the cycle'
}

$StartedAt = [DateTime]::UtcNow
$CyclePassed = $false
try {
    New-SecureStaging
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
    if ((Get-ArtifactTreeSha256 $SecureArtifactDir) -cne $ExpectedArtifactTreeSha256) {
        throw 'secure artifact tree changed during the Windows Service cycle'
    }
    $CyclePassed = $true
} finally {
    Invoke-OwnedCleanup -AllowCreatedThisRun
}

$After = Get-Residue
if (!$CyclePassed -or $After.service -or $After.registry -or $After.state -or $After.secure_staging -or $After.process_count -or $After.listener_count) {
    throw 'Windows Service cycle failed or left residue'
}
[ordered]@{
    contract = 'v01-008/1'
    status = 'passed'
    target = 'windows_service'
    source_revision = $ExpectedSourceRevision
    artifact_tree_sha256 = $ExpectedArtifactTreeSha256
    runtime_evidence_binding_sha256 = $ExpectedRuntimeEvidenceBindingSha256
    identity = 'LocalService'
    elapsed_ms = [int]([DateTime]::UtcNow - $StartedAt).TotalMilliseconds
    residue = $After
} | ConvertTo-Json -Depth 4 -Compress
