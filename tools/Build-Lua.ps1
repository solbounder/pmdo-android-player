[CmdletBinding()]
param(
    [ValidateSet('arm64-v8a','x86_64')][string]$Abi = 'arm64-v8a',
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\.cache\upstream\PMDODump')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path ([IO.Path]::GetFullPath($SourceRoot)) 'PMDC\RogueEssence\NLua\KeraLua\external\lua\src'
$include = Join-Path (Split-Path $source) 'include'
$ndk = Join-Path $env:LOCALAPPDATA 'Android\Sdk\ndk\29.0.14206865'
if (-not (Test-Path $ndk)) { throw "Android NDK 29.0.14206865 is required at $ndk" }
if (-not (Test-Path (Join-Path $source 'lapi.c'))) { throw "Lua source not found at $source" }

$triple = if ($Abi -eq 'arm64-v8a') { 'aarch64-linux-android33' } else { 'x86_64-linux-android33' }
$clang = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\clang.exe'
$output = Join-Path $repoRoot ".artifacts\lua\libs\$Abi\liblua54.so"
New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
$sources = Get-ChildItem $source -Filter '*.c' | Where-Object Name -NotIn @('lua.c','luac.c','onelua.c') | ForEach-Object FullName

$arguments = @(
    "--target=$triple", "-I$include", '-O2', '-fPIC', '-shared', '-DLUA_USE_LINUX',
    '-Wl,-soname,liblua54.so', '-Wl,-z,max-page-size=16384', '-Wl,-z,common-page-size=16384',
    '-o', $output
) + @($sources) + @('-lm', '-ldl')
& $clang @arguments
if ($LASTEXITCODE -ne 0) { throw 'Lua native build failed.' }
Write-Host "Built $output"
