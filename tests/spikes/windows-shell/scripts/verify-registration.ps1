[CmdletBinding()]
param(
  [string] $DllPath = (Join-Path $PSScriptRoot '..\build\bin\Release\AssetShellExtension.dll')
)
$ErrorActionPreference = 'Stop'
$clsid = '{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}'
$classes = "HKCU:\Software\Classes\CLSID\$clsid"
$inproc = Join-Path $classes 'InprocServer32'
$namespace = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$clsid"
$expected = (Resolve-Path $DllPath).Path
$classRegistration = Get-ItemProperty -LiteralPath $classes
$registration = Get-ItemProperty -LiteralPath $inproc
if ($classRegistration.AssetLibraryOwner -ne 'AssetLibrary.M0-002') {
  throw 'CLSID root owner marker is missing.'
}
if ($registration.AssetLibraryOwner -ne 'AssetLibrary.M0-002') { throw 'Owner marker is missing.' }
if ((Resolve-Path $registration.'(default)').Path -ne $expected) {
  throw 'InprocServer32 path does not match the checked-in binary.'
}
$namespaceRegistration = Get-ItemProperty -LiteralPath $namespace
if ($namespaceRegistration.AssetLibraryOwner -ne 'AssetLibrary.M0-002') {
  throw 'Desktop namespace owner marker is missing.'
}
if ($namespaceRegistration.'(default)' -ne 'AssetLibrary M0-002') { throw 'Desktop namespace name does not match.' }
Write-Host 'HKCU registration is present and owned by M0-002.'
