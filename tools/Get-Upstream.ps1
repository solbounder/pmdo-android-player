[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\.cache\upstream\PMDODump'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content (Join-Path $repoRoot 'upstream.lock.json') -Raw | ConvertFrom-Json
$destinationPath = [IO.Path]::GetFullPath($Destination)
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.cache\upstream'))
if (-not $destinationPath.StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Destination must remain below $cacheRoot"
}

if ((Test-Path $destinationPath) -and $Force) {
    Remove-Item -LiteralPath $destinationPath -Recurse -Force
}
if (-not (Test-Path $destinationPath)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $destinationPath) | Out-Null
    git clone --no-checkout $lock.repository $destinationPath
}

git -C $destinationPath fetch --tags --force origin
git -C $destinationPath checkout --detach $lock.commit
git -C $destinationPath submodule sync --recursive
git -C $destinationPath submodule update --init --recursive --force

$roguePath = Join-Path $destinationPath 'PMDC\RogueEssence'
git -C $roguePath checkout --detach (($lock.submodules | Where-Object path -eq 'PMDC/RogueEssence').commit)
$amState = (git -C $roguePath rev-parse --git-path rebase-apply).Trim()
if (Test-Path -LiteralPath $amState) {
    git -C $roguePath am --abort
}
if ($null -eq $lock.patchCommitter -or
    [string]::IsNullOrWhiteSpace([string]$lock.patchCommitter.name) -or
    [string]::IsNullOrWhiteSpace([string]$lock.patchCommitter.email)) {
    throw 'upstream.lock.json must define a pseudonymous patchCommitter.'
}
$previousCommitterName = $env:GIT_COMMITTER_NAME
$previousCommitterEmail = $env:GIT_COMMITTER_EMAIL
try {
    $env:GIT_COMMITTER_NAME = [string]$lock.patchCommitter.name
    $env:GIT_COMMITTER_EMAIL = [string]$lock.patchCommitter.email
    Get-ChildItem (Join-Path $repoRoot 'patches\rogueessence\*.patch') | Sort-Object Name | ForEach-Object {
        git -C $roguePath am --3way --committer-date-is-author-date $_.FullName
        if ($LASTEXITCODE -ne 0) { throw "Failed to apply $($_.Name)." }
    }
}
finally {
    $env:GIT_COMMITTER_NAME = $previousCommitterName
    $env:GIT_COMMITTER_EMAIL = $previousCommitterEmail
}

& (Join-Path $PSScriptRoot 'Verify-Upstream.ps1') -Root $destinationPath
Write-Host "Locked upstream is ready at $destinationPath"
