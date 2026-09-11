# Definitions only: sourcing this file does not query or write registry/process state.
function Get-ProofExecutionContext {
    try {
        if (-not ('AssetLibraryExplorerProof.ExecutionContextReader' -as [type])) {
            Add-Type -Path (Join-Path $PSScriptRoot 'NativeExecutionContext.cs')
        }
        $self = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -OperationTimeoutSec 5
        if ($null -eq $self) { throw 'Current process context unavailable' }
        [AssetLibraryExplorerProof.ExecutionContextReader]::Read([uint32]$self.ParentProcessId)
    } catch {
        [pscustomobject]@{ QuerySucceeded=$false; ErrorType=$_.Exception.GetType().Name }
    }
}

function Test-ProofNativeLaunch {
    param([Parameter(Mandatory)]$Context)
    # No package identity alone does not establish a non-virtualized registry view.
    foreach ($property in @('QuerySucceeded','Is64Bit','PackageIdentityAbsent','ParentIsSystemExplorer','SameUser','SameSession','ParentPredatesExecutor','OrdinaryUser')) {
        $evidence = $Context.PSObject.Properties[$property]
        if ($null -eq $evidence -or $evidence.Value -isnot [bool] -or -not $evidence.Value) { return $false }
    }
    return $true
}

function Assert-ProofNativeLaunch {
    param([Parameter(Mandatory)]$Context)
    if (-not (Test-ProofNativeLaunch -Context $Context)) {
        throw 'Native Explorer launch required for registration tests. Use an observed system Explorer ShellFolderView.Application.ShellExecute with ordinary user permissions; current-process HKCU or no package identity does not prove system Explorer visibility.'
    }
}

function New-ProofRegistryReport {
    param([Parameter(Mandatory)]$Context, [bool]$ClassPresent, [bool]$NamespacePresent, [AllowNull()]$Owner)
    [pscustomobject]@{
        ClassPresent=$ClassPresent
        NamespacePresent=$NamespacePresent
        Owner=$Owner
        RegistryView='current-process'
        NativeLaunchRouteVerified=(Test-ProofNativeLaunch -Context $Context)
        SystemExplorerRegistrationVerified=$false
        SystemExplorerCleanupVerified=$false
        ExecutionContext=$Context
    }
}
