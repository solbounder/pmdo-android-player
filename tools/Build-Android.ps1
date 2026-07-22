[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet_home'
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
$signingKeyStore = Join-Path $env:LOCALAPPDATA 'Xamarin\Mono for Android\debug.keystore'
if (-not (Test-Path -LiteralPath $signingKeyStore)) {
    throw "The update signing key is missing: $signingKeyStore"
}
$signingProperties = @(
    '-p:AndroidKeyStore=true',
    "-p:AndroidSigningKeyStore=$signingKeyStore",
    '-p:AndroidSigningStorePass=android',
    '-p:AndroidSigningKeyAlias=androiddebugkey',
    '-p:AndroidSigningKeyPass=android'
)

$sdkVersion = (& $dotnet --version).Trim()
if ($sdkVersion -ne '10.0.302') { throw "Expected .NET SDK 10.0.302, got $sdkVersion." }

& (Join-Path $PSScriptRoot 'Verify-Upstream.ps1')
& (Join-Path $PSScriptRoot 'New-RuntimeManifest.ps1') -Check
& (Join-Path $PSScriptRoot 'Test-AudioVolumeBounds.ps1')
& (Join-Path $PSScriptRoot 'Build-Lua.ps1')
& $dotnet restore (Join-Path $repoRoot 'PMDO.Android.slnx') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnet clean (Join-Path $repoRoot 'src\PMDO.Android\PMDO.Android.csproj') -c $Configuration -r android-arm64 -p:RuntimeIdentifiers=android-arm64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
& $dotnet build (Join-Path $repoRoot 'src\PMDO.Android\PMDO.Android.csproj') -c $Configuration -r android-arm64 --no-restore -p:RuntimeIdentifiers=android-arm64 -p:PublishTrimmed=false -p:RunAOTCompilation=false @signingProperties
if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
& (Join-Path $PSScriptRoot 'Test-Apk.ps1') -Configuration $Configuration
