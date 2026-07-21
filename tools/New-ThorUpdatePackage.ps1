[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\release\PMDO-Android-Player-Thor-Update-v0.1.4.zip'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$apkEntryName = 'PMDO-Android-Player-v0.1.4.apk'
$items = @(
    (Join-Path $repoRoot 'src\PMDO.Android\bin\Release\net10.0-android\android-arm64\io.github.solbounder.pmdoandroid-Signed.apk'),
    (Join-Path $repoRoot 'docs\THOR-UPDATE.txt')
)
foreach ($item in $items) {
    if (-not (Test-Path -LiteralPath $item)) { throw "Package source is missing: $item" }
}

$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) {
    if (-not $Force) { throw "Destination already exists: $destinationPath (use -Force to replace it)" }
    Remove-Item -LiteralPath $destinationPath -Force
}

$output = [IO.File]::Open($destinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$archive = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($item in $items) {
        $file = Get-Item -LiteralPath $item
        $entryName = if ($file.Extension -ieq '.apk') { $apkEntryName } else { $file.Name }
        $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $file.LastWriteTime
        $entryStream = $entry.Open()
        $input = $file.OpenRead()
        try { $input.CopyTo($entryStream) }
        finally { $input.Dispose(); $entryStream.Dispose() }
    }
}
finally { $archive.Dispose() }

Write-Host "Thor update package created: $destinationPath"
