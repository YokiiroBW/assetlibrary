[CmdletBinding()]
param(
  [ValidateSet('normal', 'slow', 'crash', 'invalid')]
  [string] $Mode = 'normal',
  [switch] $Once
)
$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $PSScriptRoot '..\build\bin\Release\AssetHostStub.exe'
$arguments = @()
if ($Once) { $arguments += '--once' }
switch ($Mode) {
  'slow' { $arguments += '--delay-ms=1000' }
  'crash' { $arguments += '--crash-after=1' }
  'invalid' { $arguments += '--invalid-response' }
}
& $hostPath @arguments
exit $LASTEXITCODE
