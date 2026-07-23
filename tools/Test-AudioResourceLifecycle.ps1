$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'src/RogueEssence.Android/Platform/LoopedSong.cs'
$source = Get-Content -Raw $sourcePath

$constructor = [regex]::Match(
    $source,
    'public LoopedSong\(string fileName\)\s*\{(?<body>.*?)\n\s*\}\s*\n\s*internal void Play',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $constructor.Success) {
    throw 'Could not locate the Android LoopedSong metadata constructor.'
}
if ($constructor.Groups['body'].Value -match 'DynamicSoundEffectInstance') {
    throw 'LoopedSong metadata construction must not reserve an Android audio source.'
}

foreach ($requiredClause in @(
    'using VorbisReader metadata = new VorbisReader\(fileName\);',
    'private void EnsurePlaybackResources\(\)',
    'createdStream = new DynamicSoundEffectInstance\(',
    'createdStream\.BufferNeeded \+= BufferNeeded;',
    '\(failedStream, failedReader\) = DetachPlaybackResources\(\);',
    'oldStream\.BufferNeeded -= BufferNeeded;',
    'oldStream\?\.Dispose\(\);',
    'oldReader\?\.Dispose\(\);')) {
    if ($source -notmatch $requiredClause) {
        throw "Android audio lifecycle contract is missing: $requiredClause"
    }
}

Write-Host 'Android audio resource lifecycle regression checks passed.'
