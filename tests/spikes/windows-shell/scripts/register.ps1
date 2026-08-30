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

$existing = Get-ItemProperty -LiteralPath $inproc -ErrorAction SilentlyContinue
if ($existing -and $existing.'(default)' -and ((Resolve-Path $existing.'(default)' -ErrorAction SilentlyContinue).Path -ne $resolvedDll)) {
  throw "Refusing to replace an existing CLSID registration owned by another path: $($existing.'(default)')"
}
if ($existing -and $existing.AssetLibraryOwner -and $existing.AssetLibraryOwner -ne $owner) {
  throw 'Refusing to replace an existing registration with a different owner marker.'
}

if ($PSCmdlet.ShouldProcess("HKCU CLSID $clsid", 'register M0-002 shell extension')) {
  New-Item -Path $classes -Force | Out-Null
  Set-ItemProperty -LiteralPath $classes -Name '(default)' -Value 'AssetLibrary M0-002 Shell Namespace'
  New-Item -Path $inproc -Force | Out-Null
  Set-ItemProperty -LiteralPath $inproc -Name '(default)' -Value $resolvedDll
  Set-ItemProperty -LiteralPath $inproc -Name ThreadingModel -Value 'Apartment'
  Set-ItemProperty -LiteralPath $inproc -Name AssetLibraryOwner -Value $owner
  New-Item -Path $shellFolder -Force | Out-Null
  New-ItemProperty -LiteralPath $shellFolder -Name Attributes -PropertyType DWord -Value 0xA0000000 -Force | Out-Null
  New-Item -Path $namespace -Force | Out-Null
  Set-ItemProperty -LiteralPath $namespace -Name '(default)' -Value 'AssetLibrary M0-002'
  Write-Host "Registered for current user only: $resolvedDll"
}
