[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$build = Join-Path $root 'build'

cmake -S $root -B $build -A x64
cmake --build $build --config $Configuration --parallel
Write-Host "Built $Configuration x64 binaries under $build\bin"
