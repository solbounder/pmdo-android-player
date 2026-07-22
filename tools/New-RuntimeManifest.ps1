[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\.cache\upstream\PMDODump\DumpAsset'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\PMDO.Android\Assets\runtime-manifest.json'),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourcePath = (Resolve-Path $Source).Path
$repositoryRoot = (& git -C $sourcePath rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "Runtime source is not a Git worktree: $sourcePath"
}

$repositoryRoot = [IO.Path]::GetFullPath($repositoryRoot.Trim())
if (-not [StringComparer]::OrdinalIgnoreCase.Equals($repositoryRoot.TrimEnd('\'), $sourcePath.TrimEnd('\'))) {
    throw "Runtime source must be the root of its Git worktree: $sourcePath"
}

# PMDOSetup installs DumpAsset from GitHub's archive for the pinned commit.
# Hash the canonical Git blobs as well, instead of the local Windows worktree,
# where Git may have converted LF text files to CRLF.
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('pmdo-runtime-manifest-' + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $temporaryDirectory 'runtime.zip'
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    & git -C $sourcePath -c core.autocrlf=false archive --format=zip --output=$archivePath HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "Could not archive the runtime source at $sourcePath"
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $files = foreach ($entry in $archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | Sort-Object FullName) {
            $stream = $entry.Open()
            $sha256 = [Security.Cryptography.SHA256]::Create()
            try {
                $hash = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
            }
            finally {
                $sha256.Dispose()
                $stream.Dispose()
            }

            [ordered]@{ path = $entry.FullName; size = $entry.Length; sha256 = $hash }
        }
    }
    finally {
        $archive.Dispose()
    }

    $manifest = [ordered]@{ version = '0.8.12'; files = @($files) }
    $outputPath = [IO.Path]::GetFullPath($Output)
    $json = $manifest | ConvertTo-Json -Depth 4 -Compress
    if ($Check) {
        if (-not (Test-Path -LiteralPath $outputPath)) {
            throw "Runtime manifest does not exist: $outputPath"
        }
        $existing = [IO.File]::ReadAllText($outputPath)
        if (-not [StringComparer]::Ordinal.Equals($existing, $json)) {
            throw 'Runtime manifest is out of date. Run tools\New-RuntimeManifest.ps1 and commit the result.'
        }
        Write-Host "Verified $($files.Count) locked runtime entries in $outputPath"
    }
    else {
        New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
        [IO.File]::WriteAllText($outputPath, $json, [Text.UTF8Encoding]::new($false))
        Write-Host "Wrote $($files.Count) locked runtime entries to $outputPath"
    }
}
finally {
    if (Test-Path -LiteralPath $archivePath) {
        [IO.File]::Delete($archivePath)
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        [IO.Directory]::Delete($temporaryDirectory)
    }
}
