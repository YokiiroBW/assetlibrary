[CmdletBinding()]
param(
  [ValidateSet('register', 'unregister', 'verify')][string]$Action = 'verify',
  [string]$DllPath
)
$ErrorActionPreference = 'Stop'
$proofOwner = 'AssetLibrary.V03-002.ExplorerProof'
$proofClsid = '{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}'
$classSubkey = "Software\Classes\CLSID\$proofClsid"
$namespaceSubkey = "Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$proofClsid"
$hive = [Microsoft.Win32.Registry]::CurrentUser
function Notify-ProofShell {
  if (-not ('AssetLibraryExplorerProof.Notification' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
namespace AssetLibraryExplorerProof {
 public static class Notification {
  [DllImport("shell32.dll")] static extern void SHChangeNotify(uint id, uint flags, IntPtr a, IntPtr b);
  [DllImport("shell32.dll")] static extern int SHGetSpecialFolderLocation(IntPtr owner, int folder, out IntPtr pidl);
  public static bool Run() {
   var thread = new Thread(() => {
    SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    IntPtr desktop;
    if(SHGetSpecialFolderLocation(IntPtr.Zero,0,out desktop)==0) {
     try { SHChangeNotify(0x00001000,0x2000,desktop,IntPtr.Zero); }
     finally { Marshal.FreeCoTaskMem(desktop); }
    }
   });
   thread.IsBackground = true; thread.Start(); return thread.Join(3000);
  }
 }
}
'@
  }
  if (-not [AssetLibraryExplorerProof.Notification]::Run()) { Write-Warning 'Shell notification exceeded 3 seconds; registry state remains authoritative.' }
}
function Remove-ProofKeys {
  foreach ($subkey in @($namespaceSubkey, $classSubkey)) {
    $key = $hive.OpenSubKey($subkey, $false)
    if ($null -eq $key) { continue }
    try {
      if ($key.GetValue('AssetLibraryOwner') -ne $proofOwner) { throw 'Refusing to remove a key owned by someone else.' }
      $children = @($key.GetSubKeyNames())
      $allowed = if ($subkey -eq $classSubkey) { @('InprocServer32','ShellFolder','DefaultIcon') } else { @() }
      foreach ($child in $children) { if ($child -notin $allowed) { throw 'Foreign subkey prevents cleanup.' } }
    } finally { $key.Dispose() }
    $hive.DeleteSubKeyTree($subkey, $false)
  }
}
if ($Action -eq 'register') {
  if ((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System').EnableLUA -ne 1) { throw 'UAC-enabled HKCU COM environment required; no changes made.' }
  $resolved = (Resolve-Path -LiteralPath $DllPath).Path
  foreach ($subkey in @($classSubkey, $namespaceSubkey)) {
    $existing = $hive.OpenSubKey($subkey, $false)
    if ($existing) { $existing.Dispose(); throw 'Existing registration: refusing replacement.' }
  }
  try {
    $key = $hive.CreateSubKey($classSubkey)
    try {
      $key.SetValue('AssetLibraryOwner', $proofOwner)
      $key.SetValue('', 'AssetLibrary 集成验证')
      $key.SetValue('System.IsPinnedToNameSpaceTree', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
      $inproc = $key.CreateSubKey('InprocServer32')
      try { $inproc.SetValue('', $resolved); $inproc.SetValue('ThreadingModel', 'Apartment') } finally { $inproc.Dispose() }
      $folder = $key.CreateSubKey('ShellFolder')
      try { $folder.SetValue('Attributes', -1476132864, [Microsoft.Win32.RegistryValueKind]::DWord) } finally { $folder.Dispose() }
      $icon = $key.CreateSubKey('DefaultIcon')
      try { $icon.SetValue('', "$env:SystemRoot\System32\shell32.dll,3") } finally { $icon.Dispose() }
    } finally { $key.Dispose() }
    $entry = $hive.CreateSubKey($namespaceSubkey)
    try { $entry.SetValue('AssetLibraryOwner', $proofOwner); $entry.SetValue('', 'AssetLibrary 集成验证') } finally { $entry.Dispose() }
    Notify-ProofShell
  } catch { Remove-ProofKeys; throw }
}
if ($Action -eq 'unregister') { Remove-ProofKeys; Notify-ProofShell }
$class = $hive.OpenSubKey($classSubkey, $false)
$entry = $hive.OpenSubKey($namespaceSubkey, $false)
try { [pscustomobject]@{ ClassPresent=($null -ne $class); NamespacePresent=($null -ne $entry); Owner=if($class){$class.GetValue('AssetLibraryOwner')}else{$null} } | ConvertTo-Json -Compress }
finally { if($class){$class.Dispose()}; if($entry){$entry.Dispose()} }
