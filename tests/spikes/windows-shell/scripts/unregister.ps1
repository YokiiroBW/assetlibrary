[CmdletBinding(SupportsShouldProcess)]
param(
  [string] $DllPath = (Join-Path $PSScriptRoot '..\build\bin\Release\AssetShellExtension.dll')
)
$ErrorActionPreference = 'Stop'
$clsid = '{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}'
$classes = "HKCU:\Software\Classes\CLSID\$clsid"
$inproc = Join-Path $classes 'InprocServer32'
$namespace = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$clsid"
# Unregistration must still work if a failed build or rollback removed the DLL.
$expected = [IO.Path]::GetFullPath($DllPath)
$registration = Get-ItemProperty -LiteralPath $inproc -ErrorAction SilentlyContinue
$namespaceRegistration = Get-ItemProperty -LiteralPath $namespace -ErrorAction SilentlyContinue
$inprocExists = $null -ne $registration
$namespaceExists = $null -ne $namespaceRegistration
$inprocOwned = $inprocExists -and
  $registration.AssetLibraryOwner -eq 'AssetLibrary.M0-002' -and
  $registration.'(default)' -and
  [IO.Path]::GetFullPath($registration.'(default)') -eq $expected
$namespaceOwned = $namespaceExists -and
  $namespaceRegistration.AssetLibraryOwner -eq 'AssetLibrary.M0-002' -and
  $namespaceRegistration.'(default)' -eq 'AssetLibrary M0-002'
if ($inprocExists -and -not $inprocOwned) {
  throw 'Refusing to remove an InprocServer32 registration with an owner/path mismatch.'
}
if ($namespaceExists -and -not $namespaceOwned) {
  throw 'Refusing to remove a Desktop namespace registration with an owner/name mismatch.'
}
if (-not $inprocExists -and -not $namespaceExists) {
  Write-Host 'M0-002 is already unregistered.'
  exit 0
}

if ($PSCmdlet.ShouldProcess("HKCU CLSID $clsid", 'unregister M0-002 shell extension')) {
  if ($namespaceOwned) {
    Remove-Item -LiteralPath $namespace -Recurse -Force
  }
  if ($inprocOwned) {
    Remove-Item -LiteralPath $classes -Recurse -Force
  }
  Write-Host 'Removed M0-002 HKCU registration. Restart Explorer to confirm recovery.'
}
