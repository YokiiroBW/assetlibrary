#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'execution-context.ps1')
$required = @('QuerySucceeded','Is64Bit','PackageIdentityAbsent','ParentIsSystemExplorer','SameUser','SameSession','ParentPredatesExecutor','OrdinaryUser')
function New-ContextFixture {
    $record = [ordered]@{}
    foreach ($name in $required) { $record[$name] = $true }
    [pscustomobject]$record
}
function Assert-Rejected($context, [string]$label) {
    if (Test-ProofNativeLaunch -Context $context) { throw "Unexpected admission: $label" }
    $rejected = $false
    try { Assert-ProofNativeLaunch -Context $context }
    catch { $rejected = $_.Exception.Message.StartsWith('Native Explorer launch required') }
    if (-not $rejected) { throw "Missing actionable rejection: $label" }
}
$valid = New-ContextFixture
if (-not (Test-ProofNativeLaunch -Context $valid)) { throw 'Validated ordinary Explorer route rejected' }
Assert-ProofNativeLaunch -Context $valid
$count = 1
foreach ($name in $required) {
    $context = New-ContextFixture
    $context.$name = $false
    Assert-Rejected $context "$name=false"
    $count++
}
# This is the regression: same user/session and no package identity are insufficient.
$inside = New-ContextFixture
$inside.ParentIsSystemExplorer = $false
$inside | Add-Member -NotePropertyName PackageIdentityStatus -NotePropertyValue 15700
Assert-Rejected $inside 'Unpackaged identity with an unverified launch origin'
$count++
foreach ($value in @($null, 'true', 1)) {
    $context = New-ContextFixture
    $context.QuerySucceeded = $value
    Assert-Rejected $context 'Malformed evidence must not coerce to Boolean'
    $count++
}
Assert-Rejected ([pscustomobject]@{}) 'Missing evidence'
$count++
foreach ($context in @($inside, $valid)) {
    foreach ($present in @($true, $false)) {
        $report = New-ProofRegistryReport -Context $context -ClassPresent $present -NamespacePresent $present -Owner 'test-owner'
        if ($report.RegistryView -cne 'current-process' -or $report.ClassPresent -ne $present -or
            $report.NamespacePresent -ne $present -or $report.SystemExplorerRegistrationVerified -or $report.SystemExplorerCleanupVerified) {
            throw 'Local presence/absence was promoted to system Explorer evidence'
        }
        if ($report.NativeLaunchRouteVerified -ne (Test-ProofNativeLaunch -Context $context)) { throw 'Launch provenance was not retained separately' }
        $count++
    }
}
$missingOwner = New-ProofRegistryReport -Context $inside -ClassPresent $false -NamespacePresent $false -Owner $null
if ($null -ne $missingOwner.Owner) { throw 'Missing owner was replaced by an empty string or default' }
$count++
# Compile both helpers and exercise only the current process token query below;
# no Shell notifications, registry access or external process query is performed.
Add-Type -Path (Join-Path $PSScriptRoot 'NativeExecutionContext.cs')
$selfRead = [AssetLibraryExplorerProof.ExecutionContextReader]::Read([uint32]$PID)
if (-not $selfRead.QuerySucceeded) { throw 'Own-token membership query failed; QUERY-only token regression' }
if (Test-ProofNativeLaunch -Context $selfRead) { throw 'A process must not certify itself as its Explorer launch origin' }
foreach ($scriptName in @('execution-context.ps1','registration.ps1','verify.ps1')) {
    $tokens = $null; $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $scriptName), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw "Invalid PowerShell syntax: $scriptName" }
    if ($scriptName -eq 'registration.ps1') {
        $gate = @($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Assert-ProofNativeLaunch'}, $true))
        $hiveAccess = @($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.MemberExpressionAst] -and $node.Static -and $node.Member.Value -ceq 'CurrentUser'}, $true))
        if ($gate.Count -ne 1 -or $hiveAccess.Count -ne 1 -or $gate[0].Extent.StartOffset -ge $hiveAccess[0].Extent.StartOffset) {
            throw 'Registration admission must precede access to the current-user registry'
        }
        $literals = @($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $node.StringConstantType -eq 'SingleQuotedHereString'}, $true))
        if ($literals.Count -ne 1) { throw 'Expected one notification implementation' }
        Add-Type -TypeDefinition $literals[0].Value
    }
    if ($scriptName -eq 'verify.ps1') {
        $gate = @($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Assert-ProofNativeLaunch'}, $true))
        $registryCalls = @($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.CommandElements[0] -is [System.Management.Automation.Language.VariableExpressionAst] -and $node.CommandElements[0].VariablePath.UserPath -ceq 'registration'}, $true))
        if ($gate.Count -ne 1 -or $registryCalls.Count -eq 0 -or $gate[0].Extent.StartOffset -ge $registryCalls[0].Extent.StartOffset) {
            throw 'Verification must reject unverified context before registration calls'
        }
    }
}
[pscustomobject]@{ ContextPolicyCases=$count; AdmissionWiringChecks=2; NativeHelpersCompiled=$true; ScriptSyntaxPassed=$true; RegistryWrites=0; ContextQueries=1; ContextQueryScope='Current process used as both query subjects; no external process or registry query'; Notifications=0; ExplorerActions=0 } | ConvertTo-Json
