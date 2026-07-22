[CmdletBinding()]
param(
    [string]$Package = (Join-Path $PSScriptRoot '..\release\PMDO-Android-Player-Thor-Update-v0.1.6.zip'),
    [string]$QuestManifest
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
if ([string]::IsNullOrWhiteSpace($QuestManifest)) {
    $QuestManifest = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'packaging\Echoes_of_the_Abyss-v0.8.2.manifest.json'
}
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$manifest = Get-Content -LiteralPath $QuestManifest -Raw -Encoding UTF8 | ConvertFrom-Json
$expected = @('PMDO-Android-Player-v0.1.6.apk', 'THOR-UPDATE.txt') +
    @($manifest.files | ForEach-Object { 'Echoes_of_the_Abyss/' + [string]$_.path })
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('eota-thor-audit-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    $stream = [IO.File]::OpenRead($packagePath)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $false)
    try {
        $names = @($archive.Entries.FullName)
        if ($names.Count -ne $expected.Count -or @($names | Group-Object -CaseSensitive | Where-Object Count -gt 1).Count -gt 0) {
            throw 'Thor update has an unexpected or duplicate entry count.'
        }
        $missing = @($expected | Where-Object { $names -cnotcontains $_ })
        if ($missing.Count -gt 0) { throw "Thor update is missing: $($missing -join ', ')" }
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.StartsWith('/') -or $entry.FullName -match '(^|/)\.\.(/|$)' -or
                $entry.LastWriteTime.DateTime -ne [datetime]::new(1980, 1, 1, 0, 0, 0)) {
                throw "Unsafe or nondeterministic ZIP entry: $($entry.FullName)"
            }
        }
        foreach ($file in $manifest.files) {
            $entry = $archive.GetEntry('Echoes_of_the_Abyss/' + [string]$file.path)
            $entryStream = $entry.Open()
            try {
                $hash = [Security.Cryptography.SHA256]::Create()
                try { $actualHash = [BitConverter]::ToString($hash.ComputeHash($entryStream)).Replace('-', '') }
                finally { $hash.Dispose() }
            } finally { $entryStream.Dispose() }
            if ($actualHash -cne [string]$file.sha256 -or $entry.Length -ne [long]$file.length) {
                throw "Packaged Quest file differs from manifest: $($file.path)"
            }
        }
        $apkPath = Join-Path $tempRoot 'PMDO-Android-Player-v0.1.6.apk'
        $apkInput = $archive.GetEntry('PMDO-Android-Player-v0.1.6.apk').Open()
        $apkOutput = [IO.File]::Create($apkPath)
        try { $apkInput.CopyTo($apkOutput) }
        finally { $apkInput.Dispose(); $apkOutput.Dispose() }
        $apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
        $guideStream = $archive.GetEntry('THOR-UPDATE.txt').Open()
        $reader = [IO.StreamReader]::new($guideStream, [Text.Encoding]::UTF8, $true)
        try { $guide = $reader.ReadToEnd() }
        finally { $reader.Dispose(); $guideStream.Dispose() }
        if (-not $guide.Contains('Version: 0.1.6 (versionCode 7)') -or -not $guide.Contains($apkHash)) {
            throw 'Packaged guide does not identify the packaged APK version and SHA-256.'
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
    & (Join-Path (Split-Path -Parent $PSCommandPath) 'Test-Apk.ps1') -Apk $apkPath
    if ($LASTEXITCODE -ne 0) { throw 'Packaged APK audit failed.' }
} finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
[pscustomobject]@{ Package = $packagePath; Entries = $expected.Count; SHA256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash; Passed = $true }
