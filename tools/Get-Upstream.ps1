[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\.cache\upstream\PMDODump'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content (Join-Path $repoRoot 'upstream.lock.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$lock.patchCommitter.name) -or
    [string]::IsNullOrWhiteSpace([string]$lock.patchCommitter.email)) {
    throw 'upstream.lock.json must pin patchCommitter.name and patchCommitter.email.'
}
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
$_oldCommitterName = $env:GIT_COMMITTER_NAME
$_oldCommitterEmail = $env:GIT_COMMITTER_EMAIL
$_oldCommitterDate = $env:GIT_COMMITTER_DATE
try {
    $env:GIT_COMMITTER_NAME = [string]$lock.patchCommitter.name
    $env:GIT_COMMITTER_EMAIL = [string]$lock.patchCommitter.email
    Remove-Item Env:GIT_COMMITTER_DATE -ErrorAction SilentlyContinue

    Get-ChildItem (Join-Path $repoRoot 'patches\rogueessence\*.patch') | Sort-Object Name | ForEach-Object {
        git -c commit.gpgsign=false -C $roguePath am --3way --committer-date-is-author-date $_.FullName
        if ($LASTEXITCODE -ne 0) {
            git -C $roguePath am --abort 2>$null
            throw "Unable to apply locked RogueEssence patch '$($_.Name)'."
        }
    }
} finally {
    if ($null -eq $_oldCommitterName) { Remove-Item Env:GIT_COMMITTER_NAME -ErrorAction SilentlyContinue }
    else { $env:GIT_COMMITTER_NAME = $_oldCommitterName }
    if ($null -eq $_oldCommitterEmail) { Remove-Item Env:GIT_COMMITTER_EMAIL -ErrorAction SilentlyContinue }
    else { $env:GIT_COMMITTER_EMAIL = $_oldCommitterEmail }
    if ($null -eq $_oldCommitterDate) { Remove-Item Env:GIT_COMMITTER_DATE -ErrorAction SilentlyContinue }
    else { $env:GIT_COMMITTER_DATE = $_oldCommitterDate }
}

& (Join-Path $PSScriptRoot 'Verify-Upstream.ps1') -Root $destinationPath
Write-Host "Locked upstream is ready at $destinationPath"
