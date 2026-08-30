[CmdletBinding()]
param(
  [string] $DllPath = (Join-Path $PSScriptRoot '..\build\bin\Release\AssetShellExtension.dll')
)
$ErrorActionPreference = 'Stop'
$clsid = '{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}'
$inproc = "HKCU:\Software\Classes\CLSID\$clsid\InprocServer32"
$namespace = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$clsid"
$expected = (Resolve-Path $DllPath).Path
$registration = Get-ItemProperty -LiteralPath $inproc
if ($registration.AssetLibraryOwner -ne 'AssetLibrary.M0-002') { throw 'Owner marker is missing.' }
if ((Resolve-Path $registration.'(default)').Path -ne $expected) { throw 'InprocServer32 path does not match the checked-in binary.' }
if (-not (Test-Path -LiteralPath $namespace)) { throw 'Desktop namespace registration is missing.' }
Write-Host 'HKCU registration is present and owned by M0-002.'
