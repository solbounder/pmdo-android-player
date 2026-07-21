[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\.cache\upstream\PMDODump\DumpAsset'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\PMDO.Android\Assets\runtime-manifest.json')
)

$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path $Source).Path
$files = foreach ($file in Get-ChildItem $sourcePath -File -Recurse | Sort-Object FullName) {
    $relative = $file.FullName.Substring($sourcePath.TrimEnd('\').Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    [ordered]@{ path = $relative; size = $file.Length; sha256 = $hash }
}
$manifest = [ordered]@{ version = '0.8.12'; files = @($files) }
$outputPath = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$json = $manifest | ConvertTo-Json -Depth 4 -Compress
[IO.File]::WriteAllText($outputPath, $json, [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($files.Count) locked runtime entries to $outputPath"
