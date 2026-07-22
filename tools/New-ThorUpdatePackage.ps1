[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\release\PMDO-Android-Player-Thor-Update-v0.1.5.zip'),
    [string]$QuestRoot = (Join-Path $PSScriptRoot '..\..\pmd-echoes-of-the-abyss\quest\Echoes_of_the_Abyss'),
    [string]$QuestManifest,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($QuestManifest)) {
    $QuestManifest = Join-Path $repoRoot 'packaging\Echoes_of_the_Abyss-v0.8.2.manifest.json'
}
$projectPath = Join-Path $repoRoot 'src\PMDO.Android\PMDO.Android.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$displayVersion = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
$versionCode = [string]$project.Project.PropertyGroup.ApplicationVersion
if ($displayVersion -cne '0.1.5' -or $versionCode -cne '6') {
    throw "Thor update requires Android 0.1.5/versionCode 6; got $displayVersion/$versionCode."
}
$apkEntryName = "PMDO-Android-Player-v$displayVersion.apk"
$questRoot = (Resolve-Path -LiteralPath $QuestRoot).Path
$questManifestPath = (Resolve-Path -LiteralPath $QuestManifest).Path
$guidePath = Join-Path $repoRoot 'docs\THOR-UPDATE.txt'
$apkPath = Join-Path $repoRoot 'src\PMDO.Android\bin\Release\net10.0-android\android-arm64\io.github.solbounder.pmdoandroid-Signed.apk'
$items = @(
    [pscustomobject]@{
        Path = $apkPath
        Prefix = ''
    },
    [pscustomobject]@{
        Path = $questRoot
        Prefix = 'Echoes_of_the_Abyss'
    },
    [pscustomobject]@{
        Path = $guidePath
        Prefix = ''
    }
)
foreach ($item in $items) {
    if (-not (Test-Path -LiteralPath $item.Path)) { throw "Package source is missing: $($item.Path)" }
}

[xml]$questManifest = Get-Content -LiteralPath (Join-Path $questRoot 'Mod.xml') -Raw
if ([string]$questManifest.Header.Namespace -cne 'echoes_of_the_abyss' -or
    [string]$questManifest.Header.Version -cne '0.8.2' -or
    [string]$questManifest.Header.GameVersion -cne '0.8.12.0' -or
    [string]$questManifest.Header.ModType -cne 'Quest') {
    throw 'Quest manifest identity/version does not match the Thor update contract.'
}

$allowedTopLevel = @('Content', 'Data', 'Licenses', 'Strings', 'ATTRIBUTION.md', 'Mod.xml')
$allowedExtensions = @('.dir', '.idx', '.json', '.lua', '.md', '.ogg', '.png', '.resx', '.rsground', '.rsmap', '.tile', '.txt', '.xml')
$forbiddenSegments = @('.git', '.vs', 'artifacts', 'bin', 'obj', 'output', 'SAVE', 'REPLAY', 'RESCUE', 'LOG', 'SCREENSHOT', 'Screenshots')
$questFiles = @(Get-ChildItem -LiteralPath $questRoot -Recurse -File)
foreach ($file in $questFiles) {
    $relative = $file.FullName.Substring($questRoot.Length + 1).Replace('\', '/')
    $parts = @($relative -split '/')
    if ($allowedTopLevel -cnotcontains $parts[0]) {
        throw "Quest package contains an unexpected top-level path: $relative"
    }
    if (@($parts | Where-Object { $forbiddenSegments -ccontains $_ }).Count -gt 0 -or
        $file.Name.StartsWith('.', [StringComparison]::Ordinal) -or
        $allowedExtensions -cnotcontains $file.Extension.ToLowerInvariant()) {
        throw "Quest package contains a forbidden file: $relative"
    }
    if ($file.Length -gt 64MB) { throw "Quest package file exceeds 64 MiB: $relative" }
}
if ($questFiles.Count -lt 400) { throw "Quest tree is unexpectedly incomplete ($($questFiles.Count) files)." }

$sourceManifest = Get-Content -LiteralPath $questManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int]$sourceManifest.schemaVersion -ne 1 -or
    [string]$sourceManifest.questNamespace -cne 'echoes_of_the_abyss' -or
    [string]$sourceManifest.questVersion -cne '0.8.2') {
    throw 'Quest source manifest identity/version is invalid.'
}
$manifestFiles = @($sourceManifest.files)
if ($manifestFiles.Count -ne $questFiles.Count -or
    @($manifestFiles.path | Group-Object -CaseSensitive | Where-Object Count -ne 1).Count -ne 0) {
    throw 'Quest source manifest does not contain one entry for every package file.'
}
$actualByPath = @{}
foreach ($file in $questFiles) {
    $relative = $file.FullName.Substring($questRoot.Length + 1).Replace('\', '/')
    $actualByPath[$relative] = $file
}
foreach ($expected in $manifestFiles) {
    $relative = [string]$expected.path
    if (-not $actualByPath.ContainsKey($relative)) { throw "Quest source manifest file is missing: $relative" }
    $actual = $actualByPath[$relative]
    if ([long]$expected.length -ne $actual.Length -or
        [string]$expected.sha256 -cne (Get-FileHash -LiteralPath $actual.FullName -Algorithm SHA256).Hash) {
        throw "Quest source differs from its reviewed manifest: $relative"
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
    if (-not $Force) { throw "Destination already exists: $destinationPath (use -Force to replace it)" }
    Remove-Item -LiteralPath $destinationPath -Force
}

$output = [IO.File]::Open($destinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$archive = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    $entries = @()
    foreach ($item in $items) {
        $source = Get-Item -LiteralPath $item.Path
        $files = if ($source.PSIsContainer) {
            @(Get-ChildItem -LiteralPath $source.FullName -Recurse -File)
        } else { @($source) }

        foreach ($file in $files) {
            $relative = if ($source.PSIsContainer) {
                $file.FullName.Substring($source.FullName.Length + 1).Replace('\', '/')
            } elseif ($file.Extension -ieq '.apk') { $apkEntryName }
            else { $file.Name }
            $entryName = if ([string]::IsNullOrEmpty($item.Prefix)) {
                $relative
            } else { $item.Prefix + '/' + $relative }
            $entries += [pscustomobject]@{ Name = $entryName; File = $file }
        }
    }
    $entries = @($entries | Sort-Object { $_.Name })
    if (@($entries.Name | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
        throw 'Thor update contains duplicate ZIP entry names.'
    }
    foreach ($sourceEntry in $entries) {
            $entry = $archive.CreateEntry($sourceEntry.Name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $entryStream = $entry.Open()
            $input = $sourceEntry.File.OpenRead()
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
    QuestFiles = $questFiles.Count
    ZipSHA256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
}
