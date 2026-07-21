[CmdletBinding()]
param([string]$Root = (Join-Path $PSScriptRoot '..\.cache\upstream\PMDODump'))

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content (Join-Path $repoRoot 'upstream.lock.json') -Raw | ConvertFrom-Json
$rootPath = (Resolve-Path $Root).Path

function Assert-Commit([string]$Path, [string]$Expected, [string]$Label) {
    $actual = (git -C $Path rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $Expected) {
        throw "$Label commit mismatch. Expected $Expected, got $actual"
    }
    Write-Host "OK  $Label  $actual"
}

Assert-Commit $rootPath $lock.commit 'PMDODump'
foreach ($entry in $lock.submodules) {
    $expected = if ($entry.path -eq 'PMDC/RogueEssence') { $entry.patchedCommit } else { $entry.commit }
    $path = Join-Path $rootPath ($entry.path -replace '/', '\')
    Assert-Commit $path $expected $entry.path
}
