#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('initialize','start','stop','status','operator')][string]$Action,
    [Parameter(Mandatory)][string]$StatePath,
    [string]$SettingsFile,
    [string]$Python,
    [string]$PostgresBin,
    [ValidateRange(1024,65535)][int]$DatabasePort = 55432,
    [ValidateSet('bootstrap','recover','initialize-key','rotate-key')][string]$OperatorAction = 'bootstrap',
    [string]$AccountName,
    [string]$DisplayName,
    [switch]$NewAttempt,
    [switch]$PasswordFromStdin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$script:TrialSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$script:Package = [IO.Path]::GetFullPath($PSScriptRoot)
$script:StateLock = $null
$script:OperatorFailure = $null
$script:RollbackShutdown = $null

function Get-SafeLocalPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or ![IO.Path]::IsPathFullyQualified($Path) -or
        $Path -match '[\x00-\x1f"<>|]' -or $Path.StartsWith('\\')) { throw 'trial_path_invalid' }
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path))
    $root = [IO.Path]::GetPathRoot($full)
    if ($full -eq $root.TrimEnd('\') -or [IO.DriveInfo]::new($root).DriveType -ne 'Fixed') { throw 'trial_local_storage_required' }
    for ($current = $full; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'trial_reparse_path_rejected' }
        }
    }
    return $full
}

function Test-Contained([string]$Parent, [string]$Child) {
    $parentFull = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent))
    $childFull = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Child))
    return $childFull.Equals($parentFull, [StringComparison]::OrdinalIgnoreCase) -or
        $childFull.StartsWith($parentFull + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-Private([string]$Path) {
    $null = Get-SafeLocalPath $Path
    $security = Get-Acl -LiteralPath $Path
    if (!$security.GetOwner([Security.Principal.SecurityIdentifier]).Equals($script:TrialSid)) { throw 'trial_state_owner_mismatch' }
    foreach ($rule in $security.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and !$rule.IdentityReference.Equals($script:TrialSid) -and
            !$rule.IdentityReference.IsWellKnown([Security.Principal.WellKnownSidType]::LocalSystemSid) -and
            !$rule.IdentityReference.IsWellKnown([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid)) { throw 'trial_state_permissions_unsafe' }
    }
}

function New-PrivateDirectory([string]$Path) {
    $null = Get-SafeLocalPath $Path
    if (Test-Path -LiteralPath $Path) { Assert-Private $Path; return }
    if (!(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($Path)) -PathType Container)) { throw 'trial_parent_directory_missing' }
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetOwner($script:TrialSid)
    $security.SetAccessRuleProtection($true,$false)
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($script:TrialSid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    [IO.FileSystemAclExtensions]::Create([IO.DirectoryInfo]::new($Path),$security)
    Assert-Private $Path
}

function Read-Json([string]$Path, [int]$MaximumBytes = 65536) {
    $null = Get-SafeLocalPath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt $MaximumBytes) { throw 'trial_input_too_large' }
    return [IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable -DateKind String
}

function Write-PrivateJson([string]$Path, [object]$Value) {
    $null = Get-SafeLocalPath $Path
    if (Test-Path -LiteralPath $Path) { Assert-Private $Path }
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.pending'
    try {
        [IO.File]::WriteAllText($temporary, (($Value | ConvertTo-Json -Depth 10 -Compress) + "`n"), [Text.UTF8Encoding]::new($false))
        Assert-Private $temporary
        [IO.File]::Move($temporary,$Path,$true)
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Assert-Package {
    $null = Get-SafeLocalPath $script:Package
    $manifestPath = Join-Path $script:Package 'package-manifest.json'
    $manifest = Read-Json $manifestPath 4194304
    if ($manifest.format_version -ne 1 -or $manifest.product -ne 'AssetLibrary/read-only-trial' -or
        $manifest.runtime_identifier -ne 'win-x64' -or $manifest.production_file_writes_enabled -ne $false -or
        $manifest.formal_release -ne $false -or $manifest.source_revision -notmatch '^[0-9a-f]{40}$') { throw 'trial_package_manifest_invalid' }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $manifest.files) {
        if ($entry.path -match '(^/|\\|:|(^|/)\.{1,2}(/|$)|[\x00-\x1f])' -or !$expected.Add($entry.path)) { throw 'trial_package_member_invalid' }
        $path = Get-SafeLocalPath (Join-Path $script:Package $entry.path)
        if (!(Test-Contained $script:Package $path) -or !(Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-Item -LiteralPath $path).Length -ne $entry.length -or $entry.sha256 -notmatch '^[0-9a-f]{64}$' -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'trial_package_integrity_failed' }
    }
    $directories = [Collections.Generic.Stack[string]]::new()
    $directories.Push($script:Package)
    while ($directories.Count -gt 0) {
        foreach ($file in Get-ChildItem -LiteralPath $directories.Pop() -Force) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'trial_package_reparse_rejected' }
            if ($file.PSIsContainer) { $directories.Push($file.FullName); continue }
            $relative = [IO.Path]::GetRelativePath($script:Package,$file.FullName).Replace('\','/')
            if ($relative -ne 'package-manifest.json' -and !$expected.Contains($relative)) { throw 'trial_package_unexpected_file' }
        }
    }
    return (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-PrivateProcess([string]$Executable, [string[]]$Arguments, [string]$InputText = '', [int]$TimeoutSeconds = 120) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $start.Environment['DOTNET_ENVIRONMENT'] = 'Production'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit(10000) | Out-Null
            throw 'trial_command_timeout'
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($output.Length -gt 65536 -or $errorOutput.Length -gt 65536) { throw 'trial_command_output_invalid' }
        return @{ exit_code = $process.ExitCode; output = $output; error_output = $errorOutput }
    } finally { $process.Dispose() }
}

function Invoke-Database([string]$DatabaseAction) {
    $result = Invoke-PrivateProcess $script:Owner.python @('-I','-B',(Join-Path $script:Package 'trial_database.py'),$DatabaseAction,'--state',$script:State)
    if ($result.exit_code -ne 0) {
        $detail = $result.output | ConvertFrom-Json -AsHashtable -DateKind String
        if ($detail.code -match '^database_[a-z_]+$') { throw $detail.code }
        throw 'trial_database_operation_failed'
    }
    return ($result.output | ConvertFrom-Json -AsHashtable -DateKind String)
}

function Assert-State {
    Assert-Private $script:State
    $ownerPath = Join-Path $script:State 'trial-owner.json'
    Assert-Private $ownerPath
    $script:Owner = Read-Json $ownerPath
    if ($script:Owner.product -ne 'AssetLibrary/read-only-trial' -or $script:Owner.format_version -ne 1 -or
        $script:Owner.owner_sid -ne $script:TrialSid.Value -or $script:Owner.state_path -ne $script:State -or
        $script:Owner.package_root -ne $script:Package -or $script:Owner.package_manifest_sha256 -ne $script:ManifestHash) { throw 'trial_state_binding_mismatch' }
    foreach ($file in $script:Owner.tools.GetEnumerator()) {
        $null = Get-SafeLocalPath $file.Key
        if ((Get-FileHash -LiteralPath $file.Key -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Value) { throw 'trial_prerequisite_integrity_changed' }
    }
    foreach ($directory in @('secrets','tls','keys','data-protection','logs','backups')) {
        $path = Join-Path $script:State $directory
        Assert-Private $path
        foreach ($file in Get-ChildItem -LiteralPath $path -File -Force) { Assert-Private $file.FullName }
    }
}

function New-TrialCertificate {
    $certificatePath = Join-Path $script:State 'tls/server.pfx'
    $passwordPath = Join-Path $script:State 'secrets/tls-password.txt'
    if ((Test-Path -LiteralPath $certificatePath) -or (Test-Path -LiteralPath $passwordPath)) {
        Assert-Private $certificatePath
        Assert-Private $passwordPath
        return
    }
    $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(36))
    $key = [Security.Cryptography.RSA]::Create(3072)
    $certificate = $null
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=AssetLibrary localhost trial',$key,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $names = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $names.AddDnsName('localhost')
        $names.AddIpAddress([Net.IPAddress]::Parse('127.0.0.1'))
        $request.CertificateExtensions.Add($names.Build())
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new('DigitalSignature,KeyEncipherment',$true))
        $usages = [Security.Cryptography.OidCollection]::new()
        $null = $usages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages,$true))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(90))
        [IO.File]::WriteAllBytes($certificatePath,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
        [IO.File]::WriteAllText($passwordPath,$password,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllBytes((Join-Path $script:State 'tls/localhost.cer'),$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    } finally { if ($certificate) { $certificate.Dispose() }; $key.Dispose(); $password = $null }
}

function Initialize-Trial {
    if (!$SettingsFile -or !$Python -or !$PostgresBin) { throw 'trial_initialize_arguments_missing' }
    $settingsPath = Get-SafeLocalPath $SettingsFile
    $settings = Read-Json $settingsPath
    $settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (Test-Path -LiteralPath (Join-Path $script:State 'trial-owner.json')) {
        Assert-State
        if ($script:Owner.settings_sha256 -ne $settingsHash -or $script:Owner.python -ne (Get-SafeLocalPath $Python) -or
            $script:Owner.postgres_bin -ne (Get-SafeLocalPath $PostgresBin) -or $script:Owner.database_port -ne $DatabasePort) { throw 'trial_initialize_reentry_mismatch' }
        if ($script:Owner.phase -eq 'ready') { return @{ status='already_initialized'; public_origin=$script:Owner.public_origin } }
    } else {
        if ((Get-ChildItem -LiteralPath $script:State -Force | Where-Object Name -ne 'trial.lock' | Measure-Object).Count -ne 0) { throw 'trial_existing_state_rejected' }
        $pythonPath = Get-SafeLocalPath $Python
        $pgPath = Get-SafeLocalPath $PostgresBin
        $pythonVersion = Invoke-PrivateProcess $pythonPath @('-I','-B','-c','import sys; print("supported" if sys.version_info >= (3,12) else "unsupported")')
        if ($pythonVersion.exit_code -ne 0 -or $pythonVersion.output.Trim() -ne 'supported') { throw 'trial_python_version_invalid' }
        $migration = Read-Json (Join-Path $script:Package 'migrations/manifest.json')
        $tools = @{ $pythonPath = (Get-FileHash -LiteralPath $pythonPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        foreach ($tool in @('initdb','pg_ctl','postgres','psql','pg_dump','pg_restore','pg_controldata')) {
            $path = Get-SafeLocalPath (Join-Path $pgPath ($tool + '.exe'))
            $version = Invoke-PrivateProcess $path @('--version')
            if ($version.exit_code -ne 0 -or $version.output -notmatch ('\b' + [regex]::Escape($migration.postgresql.verified_patch) + '\b')) { throw 'trial_postgresql_version_invalid' }
            $tools[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $originText = if ($settings.ContainsKey('public_origin')) { $settings.public_origin } else { 'https://localhost:5443' }
        $origin = [uri]$originText
        if (!$origin.IsAbsoluteUri -or $origin.Scheme -ne 'https' -or $origin.Host -notin @('localhost','127.0.0.1') -or
            $origin.AbsolutePath -ne '/' -or $origin.Query -or $origin.Fragment -or $origin.UserInfo -or $origin.Port -lt 1024 -or $origin.Port -eq $DatabasePort) { throw 'trial_origin_invalid' }
        if ($settings.storage_sources.Count -lt 1 -or $settings.storage_sources.Count -gt 32) { throw 'trial_storage_sources_invalid' }
        $sources = @()
        $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($source in $settings.storage_sources) {
            if ($source.source_key -notmatch '^[a-z0-9_-]{1,64}$' -or !$keys.Add($source.source_key) -or
                [string]::IsNullOrWhiteSpace($source.display_name) -or $source.display_name.Length -gt 200 -or
                ![IO.Path]::IsPathFullyQualified($source.allowed_root) -or $source.allowed_root -match '[\x00-\x1f]') { throw 'trial_storage_sources_invalid' }
            $root = [IO.Path]::GetFullPath($source.allowed_root)
            foreach ($other in @($script:State,$script:Package) + @($sources | ForEach-Object allowed_root)) {
                if ((Test-Contained $other $root) -or (Test-Contained $root $other)) { throw 'trial_storage_boundary_overlap' }
            }
            $sources += @{ source_key=$source.source_key; display_name=$source.display_name; allowed_root=$root; storage_source_id=[guid]::NewGuid().ToString('D'); case_sensitive=$false }
        }
        foreach ($directory in @('secrets','tls','keys','data-protection','logs','backups')) { New-PrivateDirectory (Join-Path $script:State $directory) }
        $script:Owner = @{ format_version=1; product='AssetLibrary/read-only-trial'; phase='preparing'; owner_sid=$script:TrialSid.Value;
            deployment_id=[guid]::NewGuid().ToString('D'); state_path=$script:State; package_root=$script:Package; package_manifest_sha256=$script:ManifestHash;
            python=$pythonPath; postgres_bin=$pgPath; database_port=$DatabasePort; public_origin=$originText; settings_sha256=$settingsHash; storage_sources=$sources; tools=$tools }
        Write-PrivateJson (Join-Path $script:State 'trial-owner.json') $script:Owner
    }
    New-TrialCertificate
    $database = Invoke-Database 'initialize'
    $connections = @{}
    foreach ($name in @('audit','gateway','library','asset','scan','task')) { $connections[$name + '_connection_file'] = Join-Path $script:State ('secrets/' + $name + '.connection') }
    $configuration = @{ format_version=1; deployment_id=$script:Owner.deployment_id; public_origin=$script:Owner.public_origin; bind_host='127.0.0.1';
        state_path=$script:State; tls_certificate_file=(Join-Path $script:State 'tls/server.pfx'); tls_certificate_password_file=(Join-Path $script:State 'secrets/tls-password.txt');
        data_protection_path=(Join-Path $script:State 'data-protection'); authorization_key_file=(Join-Path $script:State 'keys/authorization.json');
        web_root=(Join-Path $script:Package 'web'); database=$connections; storage_sources=$script:Owner.storage_sources }
    Write-PrivateJson (Join-Path $script:State 'trial.json') $configuration
    $pg = Invoke-Database 'start'
    try {
        if (!(Test-Path -LiteralPath $configuration.authorization_key_file)) {
            $key = Invoke-PrivateProcess $script:HostExecutable @('--trial-operator',(Join-Path $script:State 'trial.json'),'initialize-key')
            if ($key.exit_code -ne 0) { throw 'trial_authorization_key_initialization_failed' }
        }
    } finally { if ($pg.status -eq 'started') { $null = Invoke-Database 'stop' } }
    $script:Owner.phase = 'ready'
    Write-PrivateJson (Join-Path $script:State 'trial-owner.json') $script:Owner
    return @{ status='initialized'; public_origin=$script:Owner.public_origin; migrations=$database.migrations; certificate_trust='manual_required'; asset_writes=$false }
}

function Get-OwnedHost {
    $recordPath = Join-Path $script:State 'host-process.json'
    if (!(Test-Path -LiteralPath $recordPath)) { return $null }
    Assert-Private $recordPath
    $record = Read-Json $recordPath
    $process = Get-Process -Id $record.pid -ErrorAction SilentlyContinue
    if (!$process) { return $null }
    if ($process.Path -ne $script:HostExecutable -or $process.Path -ne $record.image -or
        $process.StartTime.ToUniversalTime().Ticks -ne $record.created_ticks) { throw 'trial_host_process_owner_mismatch' }
    return $process
}

function Start-Trial {
    $running = Get-OwnedHost
    if ($running) { $running.Dispose(); throw 'trial_already_running' }
    $pg = Invoke-Database 'start'
    $process = $null
    $owned = $false
    try {
        $configuration = Join-Path $script:State 'trial.json'
        $launched = Invoke-PrivateProcess $script:Owner.python @('-I','-B',(Join-Path $script:Package 'trial_process.py'),'--state',$script:State)
        if ($launched.exit_code -ne 0) { throw 'trial_host_start_failed' }
        $identity = $launched.output | ConvertFrom-Json -AsHashtable -DateKind String
        $process = Get-Process -Id $identity.pid
        $created = [DateTime]::FromFileTimeUtc($identity.created).Ticks
        if ($process.Path -ne $script:HostExecutable -or $process.Path -ne $identity.image -or $process.StartTime.ToUniversalTime().Ticks -ne $created) { throw 'trial_host_process_owner_mismatch' }
        $owned = $true
        Write-PrivateJson (Join-Path $script:State 'host-process.json') @{ pid=$process.Id; image=$script:HostExecutable; created_ticks=$created }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            if ($process.HasExited) { throw 'trial_host_start_failed' }
            $health = Invoke-PrivateProcess $script:HostExecutable @('--trial-health-probe',$configuration) '' 10
            if ($health.exit_code -eq 0) { return @{ status='running'; public_origin=$script:Owner.public_origin; asset_writes=$false } }
            Start-Sleep -Milliseconds 250
        } while ($watch.Elapsed.TotalSeconds -lt 30)
        throw 'trial_host_readiness_timeout'
    } catch {
        if ($owned -and $process -and !$process.HasExited) { $script:RollbackShutdown = Stop-VerifiedHost $process }
        if ($pg.status -eq 'started') { $null = Invoke-Database 'stop' }
        throw
    } finally { if ($process) { $process.Dispose() } }
}

function Stop-VerifiedHost([Diagnostics.Process]$Process) {
    $originalId = $Process.Id
    $originalStart = $Process.StartTime.ToUniversalTime().Ticks
    try {
        $requested = Invoke-PrivateProcess $script:HostExecutable @('--trial-stop',(Join-Path $script:State 'trial.json')) '' 10
    } catch {
        $requested = @{ exit_code = -1 }
    }
    if ($Process.WaitForExit(45000)) {
        return $(if ($requested.exit_code -eq 0 -and $Process.ExitCode -eq 0) { 'graceful' } else { 'exited' })
    }
    $verified = Get-Process -Id $originalId -ErrorAction SilentlyContinue
    if (!$verified) { return 'exited' }
    try {
        if ($verified.Path -ne $script:HostExecutable -or $verified.StartTime.ToUniversalTime().Ticks -ne $originalStart) { throw 'trial_host_process_owner_mismatch' }
        $verified.Kill($true)
        if (!$verified.WaitForExit(10000)) { throw 'trial_host_stop_timeout' }
    } finally { $verified.Dispose() }
    return 'forced'
}

function Stop-Trial {
    $process = Get-OwnedHost
    $mode = 'already_stopped'
    if ($process) {
        try { $mode = Stop-VerifiedHost $process } finally { $process.Dispose() }
    }
    $null = Invoke-Database 'stop'
    return @{ status='stopped'; shutdown=$mode; persistent_state='preserved'; certificate_cleanup=$(if ($mode -eq 'graceful') {'normal_exit'} else {'not_confirmed'}) }
}

function Invoke-Operator {
    $inputText = ''
    if ($OperatorAction -in @('bootstrap','recover')) {
        if (!$AccountName -or ($OperatorAction -eq 'bootstrap' -and !$DisplayName)) { throw 'trial_operator_account_missing' }
        $attemptPath = Join-Path $script:State 'operator-attempt.json'
        if ((Test-Path -LiteralPath $attemptPath) -and !$NewAttempt) {
            Assert-Private $attemptPath
            $attempt = Read-Json $attemptPath
            if ($attempt.action -ne $OperatorAction -or $attempt.request.account_name -ne $AccountName -or
                ($OperatorAction -eq 'bootstrap' -and $attempt.request.display_name -ne $DisplayName)) { throw 'trial_operator_attempt_conflict' }
            if ([DateTimeOffset]::Parse($attempt.request.expires_at) -le [DateTimeOffset]::UtcNow) { throw 'trial_operator_attempt_expired_use_new_attempt' }
        } else {
            $request = @{ authorization_id=[guid]::NewGuid().ToString('D'); operation_id=[guid]::NewGuid().ToString('D'); account_name=$AccountName; expires_at=[DateTimeOffset]::UtcNow.AddMinutes(10).ToString('O') }
            if ($OperatorAction -eq 'bootstrap') { $request.display_name=$DisplayName }
            $attempt = @{ action=$OperatorAction; request=$request }
            Write-PrivateJson $attemptPath $attempt
        }
        if ($PasswordFromStdin) {
            $characters = [char[]]::new(16385)
            $length = [Console]::In.ReadBlock($characters,0,$characters.Length)
            if ($length -gt 16384) { throw 'trial_operator_password_too_large' }
            $password = [string]::new($characters,0,$length).TrimEnd("`r","`n")
            [Array]::Clear($characters)
        } else {
            $secure = Read-Host '请输入管理员口令（至少15个字符，输入不会显示）' -AsSecureString
            $password = [Net.NetworkCredential]::new('', $secure).Password
            $secure.Dispose()
        }
        $attempt.request.password=$password
        $inputText = $attempt.request | ConvertTo-Json -Compress
        $password=$null
    }
    $pg = Invoke-Database 'start'
    try {
        $result = Invoke-PrivateProcess $script:HostExecutable @('--trial-operator',(Join-Path $script:State 'trial.json'),$OperatorAction) $inputText 30
        if ([string]::IsNullOrWhiteSpace($result.output)) { throw 'trial_operator_result_missing' }
        try { $response = $result.output | ConvertFrom-Json -AsHashtable -DateKind String } catch { throw 'trial_operator_result_invalid' }
        if ($result.exit_code -ne 0) {
            if ($response.ContainsKey('message') -and $response.ContainsKey('code')) { $script:OperatorFailure=$response }
            throw 'trial_operator_rejected'
        }
        if ($response.outcome -ne 'applied') { throw 'trial_operator_result_invalid' }
        return $response
    } finally { $inputText=$null; if ($pg.status -eq 'started') { $null = Invoke-Database 'stop' } }
}

try {
    if (!$IsWindows -or ![Environment]::Is64BitProcess) { throw 'trial_windows_x64_required' }
    $script:State = Get-SafeLocalPath $StatePath
    if ($script:State.Length -gt 140) { throw 'trial_state_path_too_long' }
    if ((Test-Contained $script:Package $script:State) -or (Test-Contained $script:State $script:Package)) { throw 'trial_state_package_overlap' }
    $script:ManifestHash = Assert-Package
    $script:HostExecutable = Join-Path $script:Package 'host/AssetLibrary.CoreServer.Host.exe'
    if ($Action -eq 'initialize') {
        New-PrivateDirectory $script:State
        if (!(Test-Path -LiteralPath (Join-Path $script:State 'trial-owner.json')) -and
            (Get-ChildItem -LiteralPath $script:State -Force | Where-Object Name -ne 'trial.lock' | Measure-Object).Count -ne 0) { throw 'trial_existing_state_rejected' }
    } else { Assert-Private $script:State }
    $lockPath = Join-Path $script:State 'trial.lock'
    $null = Get-SafeLocalPath $lockPath
    $script:StateLock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    if ($Action -eq 'initialize') { $result=Initialize-Trial } else {
        Assert-State
        if ($script:Owner.phase -ne 'ready') { throw 'trial_initialization_incomplete' }
        Assert-Private (Join-Path $script:State 'trial.json')
        $result = switch ($Action) {
            'start' { Start-Trial }
            'stop' { Stop-Trial }
            'operator' { Invoke-Operator }
            'status' {
                $process=Get-OwnedHost
                if (!$process) { @{status='stopped'} } else {
                    $process.Dispose()
                    $health=Invoke-PrivateProcess $script:HostExecutable @('--trial-health-probe',(Join-Path $script:State 'trial.json')) '' 10
                    @{status=$(if ($health.exit_code -eq 0) {'ready'} else {'unavailable'}); public_origin=$script:Owner.public_origin}
                }
            }
        }
    }
    Write-Output ($result | ConvertTo-Json -Depth 6 -Compress)
    exit 0
} catch {
    $code=$_.Exception.Message
    if ($code -notmatch '^(trial|database)_[a-z_]+$') { $code='trial_operation_failed' }
    $failure=@{status='failed';code=$code}
    if ($script:RollbackShutdown) { $failure.shutdown=$script:RollbackShutdown; $failure.certificate_cleanup=$(if ($script:RollbackShutdown -eq 'graceful') {'normal_exit'} else {'not_confirmed'}) }
    if ($script:OperatorFailure) { $failure.message=$script:OperatorFailure.message; $failure.reason=$script:OperatorFailure.code }
    Write-Output ($failure | ConvertTo-Json -Compress)
    exit 1
} finally { if ($script:StateLock) { $script:StateLock.Dispose() } }
