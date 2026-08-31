param(
    [string]$DotnetDir = $env:M0_004_DOTNET_DIR
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSCommandPath
$Repo = [IO.Path]::GetFullPath((Join-Path $Root '..\..\..'))
$Runtime = Join-Path $Repo '.runtime\sandbox-storage\M0-004'
if ([string]::IsNullOrWhiteSpace($DotnetDir)) {
    $DotnetDir = Join-Path $Runtime 'dotnet-10.0.111'
}
$DotnetDir = [IO.Path]::GetFullPath($DotnetDir)
$Dotnet = Join-Path $DotnetDir 'dotnet.exe'
$Installer = Join-Path $Runtime 'dotnet-install.ps1'
$Project = Join-Path $Root 'src\ServerPackagingSpike.csproj'
$Obj = Join-Path $Runtime 'obj'
$Bin = Join-Path $Runtime 'bin'
$Artifact = Join-Path $Runtime 'artifact'

New-Item -ItemType Directory -Force -Path $Runtime | Out-Null
if (!(Test-Path -LiteralPath $Dotnet)) {
    if (!(Test-Path -LiteralPath $Installer)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $Installer
    }
    & $Installer -Version '10.0.111' -InstallDir $DotnetDir -NoPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet-install.ps1 failed ($LASTEXITCODE)" }
}

$env:DOTNET_ROOT = $DotnetDir
$env:DOTNET_CLI_HOME = Join-Path $Runtime 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $Runtime 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
Write-Output "SDK: $(& $Dotnet --version)"

$Dirty = & git -C $Repo status --porcelain --untracked-files=all
if ($LASTEXITCODE -ne 0) { throw 'git status failed' }
if ($Dirty) { throw 'working tree must be clean before provenance build' }

& git -C $Repo diff --quiet -- tests/spikes/server-packaging .codex/tasks/M0-004.md
if ($LASTEXITCODE -ne 0) { throw 'uncommitted issuance inputs' }
$SourceCommit = (& git -C $Repo log -1 --format=%H -- tests/spikes/server-packaging .codex/tasks/M0-004.md).Trim()
if ($LASTEXITCODE -ne 0 -or !$SourceCommit) { throw 'failed to resolve source commit' }
$RepositoryHead = (& git -C $Repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or !$RepositoryHead) { throw 'failed to resolve repository HEAD' }

function Stop-BuildServers {
    & $Dotnet build-server shutdown
    if ($LASTEXITCODE -ne 0) { throw "dotnet build-server shutdown failed ($LASTEXITCODE)" }
}

$RuntimePrefix = [IO.Path]::GetFullPath($Runtime).TrimEnd('\') + '\'
foreach ($Path in @($Obj, $Bin, $Artifact)) {
    $FullPath = [IO.Path]::GetFullPath($Path)
    if (!$FullPath.StartsWith($RuntimePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing unexpected cleanup path: $FullPath"
    }
    if (Test-Path -LiteralPath $FullPath) {
        [IO.Directory]::Delete($FullPath, $true)
    }
}

$BuildProperties = @(
    '-p:ContinuousIntegrationBuild=true',
    '-p:Deterministic=true',
    '-p:DeterministicSourcePaths=true',
    '-p:IncludeSourceRevisionInInformationalVersion=true',
    '-p:UseSharedCompilation=false',
    "-p:SourceRevisionId=$SourceCommit",
    "-p:PathMap=$Repo=/_/src"
)

Stop-BuildServers
try {
    foreach ($Rid in @('linux-x64', 'win-x64')) {
        $RidObj = Join-Path $Obj $Rid
        $RidBin = Join-Path $Bin $Rid
        $Output = Join-Path $Artifact $Rid
        & $Dotnet restore $Project -r $Rid --packages $env:NUGET_PACKAGES --disable-build-servers @BuildProperties "-p:BaseIntermediateOutputPath=$RidObj\" "-p:OutputPath=$RidBin\"
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore $Rid failed ($LASTEXITCODE)" }
        & $Dotnet publish $Project -c Release -r $Rid --self-contained true --no-restore --disable-build-servers -p:DebugType=None -p:DebugSymbols=false @BuildProperties "-p:BaseIntermediateOutputPath=$RidObj\" "-p:OutputPath=$RidBin\" -o $Output
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $Rid failed ($LASTEXITCODE)" }
    }
} finally {
    Stop-BuildServers
}

$Utf8NoBom = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $Artifact 'source-commit.txt'), "$SourceCommit`n", $Utf8NoBom)
[IO.File]::WriteAllText((Join-Path $Artifact 'repository-head.txt'), "$RepositoryHead`n", $Utf8NoBom)
$Files = Get-ChildItem -LiteralPath (Join-Path $Artifact 'linux-x64'), (Join-Path $Artifact 'win-x64') -File -Recurse |
    Sort-Object FullName
$SizeLines = [Collections.Generic.List[string]]::new()
$HashLines = [Collections.Generic.List[string]]::new()
foreach ($File in $Files) {
    $Relative = [IO.Path]::GetRelativePath($Artifact, $File.FullName).Replace('\', '/')
    $Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $File.FullName).Hash.ToLowerInvariant()
    $SizeLines.Add("$Relative $($File.Length) bytes")
    $HashLines.Add("$Hash  $Relative")
}
[IO.File]::WriteAllText((Join-Path $Artifact 'file-sizes.txt'), ($SizeLines -join "`n") + "`n", $Utf8NoBom)
[IO.File]::WriteAllText((Join-Path $Artifact 'files.sha256'), ($HashLines -join "`n") + "`n", $Utf8NoBom)

$LinuxExe = Join-Path $Artifact 'linux-x64\ServerPackagingSpike'
$WindowsExe = Join-Path $Artifact 'win-x64\ServerPackagingSpike.exe'
$SelectedHashLines = @(
    "$((Get-FileHash -Algorithm SHA256 -LiteralPath $LinuxExe).Hash.ToLowerInvariant())  linux-x64/ServerPackagingSpike",
    "$((Get-FileHash -Algorithm SHA256 -LiteralPath $WindowsExe).Hash.ToLowerInvariant())  win-x64/ServerPackagingSpike.exe"
)
[IO.File]::WriteAllText((Join-Path $Artifact 'hashes.txt'), ($SelectedHashLines -join "`n") + "`n", $Utf8NoBom)
$Aggregate = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Artifact 'files.sha256')).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $Artifact 'aggregate-sha256.txt'), "$Aggregate`n", $Utf8NoBom)

Get-Item -LiteralPath $LinuxExe, $WindowsExe | ForEach-Object {
    Write-Output "$($_.FullName) $($_.Length) bytes"
}
