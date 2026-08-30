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
if (-not $registration) { Write-Host 'M0-002 is already unregistered.'; exit 0 }
if ($registration.AssetLibraryOwner -ne 'AssetLibrary.M0-002') { throw 'Refusing to remove a registration without the M0-002 owner marker.' }
if ([IO.Path]::GetFullPath($registration.'(default)') -ne $expected) { throw 'Refusing to remove a registration owned by another binary.' }

if ($PSCmdlet.ShouldProcess("HKCU CLSID $clsid", 'unregister M0-002 shell extension')) {
  Remove-Item -LiteralPath $namespace -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $classes -Recurse -Force
  Write-Host 'Removed M0-002 HKCU registration. Restart Explorer to confirm recovery.'
}
