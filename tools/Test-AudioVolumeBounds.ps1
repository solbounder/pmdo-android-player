$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$audioVolume = Join-Path $root 'src/RogueEssence.Android/Platform/AudioVolume.cs'
$loopedSong = Join-Path $root 'src/RogueEssence.Android/Platform/LoopedSong.cs'
$soundManager = Join-Path $root 'src/RogueEssence.Android/Platform/SoundManager.cs'
$sources = @($loopedSong, $soundManager)

$audioVolumeText = Get-Content -Raw $audioVolume
foreach ($requiredClause in @(
    'if \(float\.IsNaN\(value\)\)\s*return 0f;',
    'if \(float\.IsPositiveInfinity\(value\)\)\s*return 1f;',
    'if \(float\.IsNegativeInfinity\(value\)\)\s*return 0f;',
    'return Math\.Max\(0f, Math\.Min\(1f, value\)\);')) {
    if ($audioVolumeText -notmatch $requiredClause) {
        throw "Volume sanitizer contract is missing: $requiredClause"
    }
}

Add-Type -Path $audioVolume
$volumeCases = @(
    @([single]::NaN, 0.0),
    @([single]::PositiveInfinity, 1.0),
    @([single]::NegativeInfinity, 0.0),
    @([single]-1.0, 0.0),
    @([single]0.0, 0.0),
    @([single]0.5, 0.5),
    @([single]1.0, 1.0),
    @([single]2.0, 1.0)
)
foreach ($case in $volumeCases) {
    $actual = [RogueEssence.Content.AudioVolume]::Sanitize($case[0])
    if ($actual -ne [single]$case[1]) {
        throw "Volume truth table expected '$($case[1])', got '$actual'."
    }
}

foreach ($source in $sources) {
    $lines = Get-Content $source
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line -match '\.Volume\s*=') {
            if ($line -notmatch 'AudioVolume\.Sanitize\(') {
                throw "Unsanitized Android audio volume assignment in $source line $($index + 1): $line"
            }
        }
    }
}

Write-Host 'Audio volume boundary regression checks passed.'
