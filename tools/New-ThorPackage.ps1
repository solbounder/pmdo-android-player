[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\release\PMDO-Android-Player-Thor-Full-v0.1.4.zip'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$apkEntryName = 'PMDO-Android-Player-v0.1.4.apk'
$items = @(
    [pscustomobject]@{
        Path = Join-Path $repoRoot 'src\PMDO.Android\bin\Release\net10.0-android\android-arm64\io.github.solbounder.pmdoandroid-Signed.apk'
        Prefix = ''
    },
    [pscustomobject]@{
        Path = Join-Path $repoRoot '.cache\upstream\PMDODump\DumpAsset'
        Prefix = 'DumpAsset'
    },
    [pscustomobject]@{
        Path = 'C:\programming_stuff\pmd-echoes-of-the-abyss\quest\Echoes_of_the_Abyss'
        Prefix = 'Echoes_of_the_Abyss'
    },
    [pscustomobject]@{
        Path = Join-Path $repoRoot 'docs\THOR-INSTALLATION.txt'
        Prefix = ''
    }
)

foreach ($item in $items) {
    if (-not (Test-Path -LiteralPath $item.Path)) { throw "Package source is missing: $($item.Path)" }
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
        $source = Get-Item -LiteralPath $item.Path
        $files = if ($source.PSIsContainer) {
            @(Get-ChildItem -LiteralPath $source.FullName -Recurse -File | Sort-Object FullName)
        }
        else { @($source) }

        foreach ($file in $files) {
            $relative = if ($source.PSIsContainer) {
                $file.FullName.Substring($source.FullName.Length + 1).Replace('\', '/')
            }
            elseif ($file.Extension -ieq '.apk') { $apkEntryName }
            else { $file.Name }
            $entryName = if ([string]::IsNullOrEmpty($item.Prefix)) { $relative } else { $item.Prefix + '/' + $relative }
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $file.LastWriteTime
            $entryStream = $entry.Open()
            $input = $file.OpenRead()
            try { $input.CopyTo($entryStream) }
            finally { $input.Dispose(); $entryStream.Dispose() }
        }
    }
}
finally { $archive.Dispose() }

Write-Host "Thor package created: $destinationPath"
