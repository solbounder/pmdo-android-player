[CmdletBinding()]
param(
    [string]$Package = (Join-Path $PSScriptRoot '..\release\PMDO-Android-Player-Thor-Update-v0.1.8.zip')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$expected = @('PMDO-Android-Player-v0.1.8.apk', 'THOR-UPDATE.txt')
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('pmdo-thor-audit-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    $stream = [IO.File]::OpenRead($packagePath)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $false)
    try {
        $names = @($archive.Entries.FullName)
        if ($names.Count -ne $expected.Count -or
            @($names | Group-Object -CaseSensitive | Where-Object Count -gt 1).Count -gt 0) {
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

        $apkPath = Join-Path $tempRoot 'PMDO-Android-Player-v0.1.8.apk'
        $apkInput = $archive.GetEntry('PMDO-Android-Player-v0.1.8.apk').Open()
        $apkOutput = [IO.File]::Create($apkPath)
        try { $apkInput.CopyTo($apkOutput) }
        finally { $apkInput.Dispose(); $apkOutput.Dispose() }
        $apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash

        $guideStream = $archive.GetEntry('THOR-UPDATE.txt').Open()
        $reader = [IO.StreamReader]::new($guideStream, [Text.Encoding]::UTF8, $true)
        try { $guide = $reader.ReadToEnd() }
        finally { $reader.Dispose(); $guideStream.Dispose() }
        if (-not $guide.Contains('Version: 0.1.8 (versionCode 9)') -or
            -not $guide.Contains($apkHash)) {
            throw 'Packaged guide does not identify the packaged APK version and SHA-256.'
        }
    }
    finally { $archive.Dispose(); $stream.Dispose() }

    & (Join-Path (Split-Path -Parent $PSCommandPath) 'Test-Apk.ps1') -Apk $apkPath
    if ($LASTEXITCODE -ne 0) { throw 'Packaged APK audit failed.' }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

[pscustomobject]@{
    Package = $packagePath
    Entries = $expected.Count
    SHA256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash
    Passed = $true
}
