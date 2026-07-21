[CmdletBinding()]
param(
    [string]$QuestRoot = (Join-Path $PSScriptRoot '..\..\pmd-echoes-of-the-abyss\quest\Echoes_of_the_Abyss'),
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $QuestRoot).Path
[xml]$questHeader = Get-Content -LiteralPath (Join-Path $root 'Mod.xml') -Raw
$questVersion = [string]$questHeader.Header.Version
if ([string]$questHeader.Header.Namespace -cne 'echoes_of_the_abyss' -or
    [string]::IsNullOrWhiteSpace($questVersion)) {
    throw 'Quest manifest identity/version is invalid.'
}
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $Destination = Join-Path $repoRoot "packaging\Echoes_of_the_Abyss-v$questVersion.manifest.json"
}
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
    [pscustomobject][ordered]@{
        path = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        length = [long]$_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
} | Sort-Object path -CaseSensitive)
if ($files.Count -lt 400) { throw "Quest tree is unexpectedly incomplete ($($files.Count) files)." }
$manifest = [ordered]@{
    schemaVersion = 1
    questNamespace = 'echoes_of_the_abyss'
    questVersion = $questVersion
    files = $files
}
$destinationPath = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory((Split-Path -Parent $destinationPath)) | Out-Null
[IO.File]::WriteAllText($destinationPath, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Destination = $destinationPath; Files = $files.Count; SHA256 = (Get-FileHash $destinationPath -Algorithm SHA256).Hash }
