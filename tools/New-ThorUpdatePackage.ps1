[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\release\PMDO-Android-Player-Thor-Update-v0.1.10.zip'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\PMDO.Android\PMDO.Android.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$displayVersion = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
$versionCode = [string]$project.Project.PropertyGroup.ApplicationVersion
if ($displayVersion -cne '0.1.10' -or $versionCode -cne '11') {
    throw "Thor update requires Android 0.1.10/versionCode 11; got $displayVersion/$versionCode."
}

$apkEntryName = "PMDO-Android-Player-v$displayVersion.apk"
$apkPath = Join-Path $repoRoot 'src\PMDO.Android\bin\Release\net10.0-android\android-arm64\io.github.solbounder.pmdoandroid-Signed.apk'
$guidePath = Join-Path $repoRoot 'docs\THOR-UPDATE.txt'
$entries = @(
    [pscustomobject]@{ Name = $apkEntryName; Path = $apkPath },
    [pscustomobject]@{ Name = 'THOR-UPDATE.txt'; Path = $guidePath }
)
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath $entry.Path)) {
        throw "Package source is missing: $($entry.Path)"
    }
}

$apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
& (Join-Path $PSScriptRoot 'Test-Apk.ps1') -Apk $apkPath
if ($LASTEXITCODE -ne 0) { throw 'Final APK audit failed.' }
$guide = [IO.File]::ReadAllText($guidePath)
if (-not $guide.Contains("Version: $displayVersion (versionCode $versionCode)") -or
    -not $guide.Contains($apkHash)) {
    throw 'THOR-UPDATE.txt does not contain the final APK version and SHA-256.'
}

$destinationPath = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory((Split-Path -Parent $destinationPath)) | Out-Null
if (Test-Path -LiteralPath $destinationPath) {
    if (-not $Force) {
        throw "Destination already exists: $destinationPath (use -Force to replace it)"
    }
    Remove-Item -LiteralPath $destinationPath -Force
}

$output = [IO.File]::Open($destinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$archive = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($sourceEntry in $entries | Sort-Object Name) {
        $entry = $archive.CreateEntry($sourceEntry.Name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $entryStream = $entry.Open()
        $input = [IO.File]::OpenRead($sourceEntry.Path)
        try { $input.CopyTo($entryStream) }
        finally { $input.Dispose(); $entryStream.Dispose() }
    }
}
finally { $archive.Dispose() }

[pscustomobject]@{
    Destination = $destinationPath
    AndroidVersion = $displayVersion
    VersionCode = [int]$versionCode
    ApkSHA256 = $apkHash
    ZipSHA256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
}
