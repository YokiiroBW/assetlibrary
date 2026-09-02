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
# Unregistration must still work if a failed build or rollback removed the DLL.
$expected = [IO.Path]::GetFullPath($DllPath)
$classRegistration = Get-ItemProperty -LiteralPath $classes -ErrorAction SilentlyContinue
$registration = Get-ItemProperty -LiteralPath $inproc -ErrorAction SilentlyContinue
$shellRegistration = Get-ItemProperty -LiteralPath $shellFolder -ErrorAction SilentlyContinue
$namespaceRegistration = Get-ItemProperty -LiteralPath $namespace -ErrorAction SilentlyContinue
$classExists = $null -ne $classRegistration
$inprocExists = $null -ne $registration
$shellFolderExists = $null -ne $shellRegistration
$namespaceExists = $null -ne $namespaceRegistration
$classOwned = $classExists -and
  $classRegistration.AssetLibraryOwner -eq 'AssetLibrary.M0-002' -and
  $classRegistration.'(default)' -eq 'AssetLibrary M0-002 Shell Namespace'
$inprocOwned = $inprocExists -and
  $registration.AssetLibraryOwner -eq 'AssetLibrary.M0-002' -and
  $registration.'(default)' -and
  [IO.Path]::GetFullPath($registration.'(default)') -eq $expected
$namespaceOwned = $namespaceExists -and
  $namespaceRegistration.AssetLibraryOwner -eq 'AssetLibrary.M0-002' -and
  $namespaceRegistration.'(default)' -eq 'AssetLibrary M0-002'

function Notify-ShellAssociationChanged([bool] $Required = $true) {
  if (-not ('AssetLibraryM0002.BoundedShellChangeNotifier' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AssetLibraryM0002
{
    public static class BoundedShellChangeNotifier
    {
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(
            uint eventId, uint flags, IntPtr item1, IntPtr item2);

        public static bool NotifyWithTimeout(int timeoutMilliseconds)
        {
            Thread thread = new Thread(delegate()
            {
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            });
            thread.IsBackground = true;
            thread.Name = "AssetLibrary M0-002 bounded shell notification";
            thread.Start();
            return thread.Join(timeoutMilliseconds);
        }
    }
}
'@
  }
  # SHCNE_ASSOCCHANGED with SHCNF_IDLIST refreshes Explorer's association cache.
  # An Explorer window can indefinitely delay the synchronous broadcast, so the
  # native call runs on a background thread and the script waits at most 3 s.
  $completed = [AssetLibraryM0002.BoundedShellChangeNotifier]::NotifyWithTimeout(3000)
  if (-not $completed) {
    $message = 'Shell association notification exceeded 3 seconds.'
    if ($Required) { throw $message }
    Write-Warning "$message Registry cleanup is already complete; sign out to refresh Explorer."
  }
}

if (($classExists -or $inprocExists -or $shellFolderExists) -and -not $classOwned) {
  throw 'Refusing to remove a CLSID tree without the expected root owner/name.'
}
if ($inprocExists -and -not $inprocOwned) {
  throw 'Refusing to remove an InprocServer32 registration with an owner/path mismatch.'
}
if ($namespaceExists -and -not $namespaceOwned) {
  throw 'Refusing to remove a Desktop namespace registration with an owner/name mismatch.'
}
if ($classOwned) {
  $unknownChildren = @(Get-ChildItem -LiteralPath $classes -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -notin @('InprocServer32', 'ShellFolder') })
  if ($unknownChildren.Count -ne 0) {
    throw 'Refusing to remove a CLSID tree containing unknown child keys.'
  }
}
if (-not $classExists -and -not $namespaceExists) {
  Write-Host 'M0-002 is already unregistered.'
  exit 0
}

if ($PSCmdlet.ShouldProcess("HKCU CLSID $clsid", 'unregister M0-002 shell extension')) {
  if ($namespaceOwned) {
    Remove-Item -LiteralPath $namespace -Recurse -Force
  }
  if ($classOwned) {
    Remove-Item -LiteralPath $classes -Recurse -Force
  }
  # Registry removal is authoritative. A delayed Explorer cache broadcast must
  # never keep cleanup or the user's terminal session alive indefinitely.
  Notify-ShellAssociationChanged -Required $false
  Write-Host 'Removed M0-002 HKCU registration. Restart Explorer to confirm recovery.'
}
