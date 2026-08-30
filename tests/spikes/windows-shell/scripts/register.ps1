[CmdletBinding(SupportsShouldProcess)]
param(
  [string] $DllPath = (Join-Path $PSScriptRoot '..\build\bin\Release\AssetShellExtension.dll')
)
$ErrorActionPreference = 'Stop'
$clsid = '{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}'
$classes = "HKCU:\Software\Classes\CLSID\$clsid"
$inproc = Join-Path $classes 'InprocServer32'
$shellFolder = Join-Path $classes 'ShellFolder'
$namespace = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$clsid"
$owner = 'AssetLibrary.M0-002'
$resolvedDll = (Resolve-Path $DllPath).Path

$createdKeys = [System.Collections.Generic.List[string]]::new()
$originalValues = @{}
$missingValues = @{}

function Track-Value([string] $path, [string] $name) {
  $key = "$path|$name"
  $missingValues[$key] = $true
  if (-not (Test-Path -LiteralPath $path)) { return }
  $item = Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
  if ($item -and $item.PSObject.Properties.Name -contains $name) {
    $missingValues[$key] = $false
    $originalValues[$key] = $item.$name
  }
}

function Ensure-Key([string] $path) {
  if (-not (Test-Path -LiteralPath $path)) {
    New-Item -Path $path -Force | Out-Null
    $createdKeys.Add($path)
  }
}

function Owned-Registration {
  $item = Get-ItemProperty -LiteralPath $inproc -ErrorAction SilentlyContinue
  if (-not $item -or $item.AssetLibraryOwner -ne $owner -or -not $item.'(default)') { return $false }
  return [IO.Path]::GetFullPath($item.'(default)') -eq $resolvedDll
}

$existingClasses = Get-ItemProperty -LiteralPath $classes -ErrorAction SilentlyContinue
if ($existingClasses -and $existingClasses.AssetLibraryOwner -ne $owner) {
  throw 'Refusing to replace a CLSID key without the M0-002 owner marker.'
}
$existing = Get-ItemProperty -LiteralPath $inproc -ErrorAction SilentlyContinue
if ($existing -and $existing.'(default)' -and [IO.Path]::GetFullPath($existing.'(default)') -ne $resolvedDll) {
  throw "Refusing to replace an existing CLSID registration owned by another path: $($existing.'(default)')"
}
$existingNamespace = Get-ItemProperty -LiteralPath $namespace -ErrorAction SilentlyContinue
if ($existingNamespace -and $existingNamespace.AssetLibraryOwner -ne $owner) {
  throw 'Refusing to replace an existing Desktop namespace key without the M0-002 owner marker.'
}

foreach ($entry in @(
  @($classes, '(default)'), @($classes, 'AssetLibraryOwner'),
  @($inproc, '(default)'), @($inproc, 'ThreadingModel'), @($inproc, 'AssetLibraryOwner'),
  @($shellFolder, 'Attributes'), @($namespace, '(default)'), @($namespace, 'AssetLibraryOwner')
)) { Track-Value $entry[0] $entry[1] }

if ($PSCmdlet.ShouldProcess("HKCU CLSID $clsid", 'register M0-002 shell extension')) {
  try {
    Ensure-Key $classes
    Ensure-Key $inproc
    # Establish the owner/path pair before creating the visible namespace entry.
    Set-ItemProperty -LiteralPath $inproc -Name '(default)' -Value $resolvedDll
    Set-ItemProperty -LiteralPath $inproc -Name AssetLibraryOwner -Value $owner
    Set-ItemProperty -LiteralPath $inproc -Name ThreadingModel -Value 'Apartment'
    Set-ItemProperty -LiteralPath $classes -Name '(default)' -Value 'AssetLibrary M0-002 Shell Namespace'
    Set-ItemProperty -LiteralPath $classes -Name AssetLibraryOwner -Value $owner
    Ensure-Key $shellFolder
    New-ItemProperty -LiteralPath $shellFolder -Name Attributes -PropertyType DWord -Value 0xA0000000 -Force | Out-Null
    Ensure-Key $namespace
    Set-ItemProperty -LiteralPath $namespace -Name AssetLibraryOwner -Value $owner
    Set-ItemProperty -LiteralPath $namespace -Name '(default)' -Value 'AssetLibrary M0-002'
    Write-Host "Registered for current user only: $resolvedDll"
  } catch {
    # Restore values first, then remove only keys created by this invocation and
    # still proven to belong to this exact owner/path pair.
    foreach ($key in $originalValues.Keys) {
      $parts = $key -split '\|', 2
      if ($missingValues[$key]) {
        Remove-ItemProperty -LiteralPath $parts[0] -Name $parts[1] -ErrorAction SilentlyContinue
      } else {
        Set-ItemProperty -LiteralPath $parts[0] -Name $parts[1] -Value $originalValues[$key] -ErrorAction SilentlyContinue
      }
    }
    $ownedBeforeRollback = Owned-Registration
    $namespaceAfterRollback = Get-ItemProperty -LiteralPath $namespace -ErrorAction SilentlyContinue
    $namespaceOwned = $namespaceAfterRollback -and $namespaceAfterRollback.AssetLibraryOwner -eq $owner
    $createdArray = $createdKeys.ToArray()
    [array]::Reverse($createdArray)
    foreach ($path in $createdArray) {
      if ((($path -eq $classes) -or ($path -eq $inproc) -or ($path -eq $shellFolder)) -and $ownedBeforeRollback) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
      } elseif (($path -eq $namespace) -and $namespaceOwned) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
    throw
  }
}
