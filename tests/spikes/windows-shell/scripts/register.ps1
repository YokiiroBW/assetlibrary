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

# With UAC disabled, administrator processes ignore per-user COM registration.
# Explorer therefore cannot bind an HKCU-only namespace extension on that host.
$uacPolicyKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey(
  'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', $false)
$enableLua = 1
if ($uacPolicyKey) {
  try {
    $configuredEnableLua = $uacPolicyKey.GetValue('EnableLUA', 1)
    if ($null -ne $configuredEnableLua) { $enableLua = [int] $configuredEnableLua }
  } finally {
    $uacPolicyKey.Dispose()
  }
}
if ($enableLua -eq 0) {
  throw ('M0-002 requires UAC (EnableLUA=1) for HKCU COM activation. ' +
    'This host has EnableLUA=0; refusing per-user registration because ' +
    'Explorer cannot bind the class. Do not switch this Spike to HKLM.')
}

$createdKeys = [System.Collections.Generic.List[string]]::new()
$originalValues = @{}
$missingValues = @{}

function Notify-ShellAssociationChanged {
  if (-not ('AssetLibraryM0002.ShellChangeNotifier' -as [type])) {
    Add-Type -Namespace AssetLibraryM0002 -Name ShellChangeNotifier -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(
  uint eventId, uint flags, System.IntPtr item1, System.IntPtr item2);
'@
  }
  # SHCNE_ASSOCCHANGED with SHCNF_IDLIST refreshes Explorer's association cache.
  [AssetLibraryM0002.ShellChangeNotifier]::SHChangeNotify(
    0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}

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

function Created-KeyCanRollback([string] $path) {
  $item = Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
  if (-not $item) { return $true }
  $children = @(Get-ChildItem -LiteralPath $path -ErrorAction SilentlyContinue)
  if ($children.Count -ne 0) { return $false }
  $hasOwner = $item.AssetLibraryOwner -eq $owner
  $hasExpectedPath = $item.'(default)' -and
    [IO.Path]::GetFullPath($item.'(default)') -eq $resolvedDll
  $userProperties = @($item.PSObject.Properties.Name | Where-Object { $_ -notlike 'PS*' })
  # A newly created empty key is also safe to remove when the first marker write failed.
  return ($hasOwner -and ($hasExpectedPath -or $path -ne $inproc)) -or
    ($hasExpectedPath -and $path -eq $inproc) -or ($userProperties.Count -eq 0)
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
    Notify-ShellAssociationChanged
    Write-Host "Registered for current user only: $resolvedDll"
  } catch {
    # Restore values first, then remove only keys created by this invocation
    # that are empty or still carry this exact owner/path pair.
    foreach ($key in $missingValues.Keys) {
      $parts = $key -split '\|', 2
      if ($missingValues[$key]) {
        Remove-ItemProperty -LiteralPath $parts[0] -Name $parts[1] -ErrorAction SilentlyContinue
      } else {
        Set-ItemProperty -LiteralPath $parts[0] -Name $parts[1] `
          -Value $originalValues[$key] -ErrorAction SilentlyContinue
      }
    }
    $createdArray = $createdKeys.ToArray()
    [array]::Reverse($createdArray)
    foreach ($path in $createdArray) {
      # createdKeys contains only keys absent before this invocation. After
      # tracked values are restored, remove a key only when it has no children
      # and contains no foreign values (or still has our exact owner/path pair).
      if (Created-KeyCanRollback $path) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
    throw
  }
}
