[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$files = @(
    'src/PMDO.Android/Android/MainActivity.cs',
    'src/PMDO.Android/Android/PlayActivity.cs',
    'src/PMDO.Android/Android/TouchOverlay.cs',
    'src/PMDO.Portable/PortableStorage.cs'
)
$files = $files | ForEach-Object { Join-Path $repoRoot $_ }
$forbidden = @(
    'Abbrechen',
    'Analysiere',
    'Auswählen',
    'Basisspiel',
    'Bereit',
    'Bitte',
    'Dateien',
    'Einfügen',
    'Fehler',
    'fotografieren',
    'kopieren',
    'löschen',
    'Ordner',
    'Schließen',
    'Sichtbar',
    'Speichern',
    'Spiel',
    'Starte',
    'Tasten',
    'Übernehmen',
    'Verarbeite',
    'Warnung',
    'Ziel für',
    'Zurück')
foreach ($file in $files) {
    $text = Get-Content -Raw -LiteralPath $file
    # Old report headings are intentionally retained only as migration inputs.
    if ($file -like '*PlayActivity.cs') { $text = $text -replace 'internal static string NormalizeErrorReport[\s\S]*?private static bool IsSavePermissionError', 'private static bool IsSavePermissionError' }
    foreach ($token in $forbidden) {
        if ($text -match [regex]::Escape($token)) { throw "German UI token '$token' found in $file" }
    }
}
$resources = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/PMDO.Android/Resources/values/strings.xml')
foreach ($token in $forbidden) {
    if ($resources -match [regex]::Escape($token)) { throw "German UI token '$token' found in Android resources." }
}
foreach ($required in @(
    'PMDO Android Player',
    'Ready',
    'PMDO base game',
    'Start game',
    'Choose destination for SAVE.rssv',
    'PMDO error on this device',
    'Edit a button, including hidden buttons')) {
    if ($resources -notmatch [regex]::Escape($required)) { throw "Missing English resource: $required" }
}
$mainActivity = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src/PMDO.Android/Android/MainActivity.cs')
foreach ($resourceName in @('button_start_game', 'button_manage_mods', 'button_import_save', 'dialog_latest_error')) {
    if ($mainActivity -notmatch "Resource\.String\.$resourceName") {
        throw "MainActivity does not use the English resource: $resourceName"
    }
}
Write-Host 'English UI source check passed.'
