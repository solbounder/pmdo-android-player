[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Apk
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$startupSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PMDO.Android\Android\GameStartup.cs') -Raw
if ($startupSource -notmatch 'initializeAndroidDisplayMode\s*\?\s*PlatformDisplay\.MaximumAspectMode') {
    throw 'Fresh Android installs must default to Max 4:3.'
}
$sdkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$buildTools = Get-ChildItem (Join-Path $sdkRoot 'build-tools') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'aapt.exe') } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if ($null -eq $buildTools) { throw 'Android build tools were not found.' }

if ([string]::IsNullOrWhiteSpace($Apk)) {
    $output = Join-Path $repoRoot "src\PMDO.Android\bin\$Configuration\net10.0-android\android-arm64"
    $candidate = Get-ChildItem $output -Filter '*-Signed.apk' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $candidate) { throw "Signed APK not found below $output" }
    $apkPath = $candidate.FullName
}
else {
    $apkPath = (Resolve-Path $Apk).Path
}

$aapt = Join-Path $buildTools.FullName 'aapt.exe'
$zipalign = Join-Path $buildTools.FullName 'zipalign.exe'
$badging = (& $aapt dump badging $apkPath) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'aapt badging check failed.' }
foreach ($expected in @("package: name='io.github.solbounder.pmdoandroid' versionCode='6' versionName='0.1.5'", "sdkVersion:'33'", "targetSdkVersion:'36'", "native-code: 'arm64-v8a'")) {
    if (-not $badging.Contains($expected)) { throw "APK badging is missing: $expected" }
}
if ($badging -notmatch "application-label:'PMDO Android Player'") { throw 'APK application label is incorrect.' }
if ($badging -notmatch "application-icon-[0-9]+:'res/[^']*app_icon\.png'") { throw 'APK launcher icon is missing.' }
if ($badging -match "native-code:.*(x86|armeabi-v7a)") { throw 'APK contains an unexpected ABI.' }

$permissions = (& $aapt dump permissions $apkPath) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'aapt permission check failed.' }
if ($permissions -match 'android\.permission\.INTERNET') { throw 'Offline v1 must not request INTERNET.' }

$manifestXml = (& $aapt dump xmltree $apkPath AndroidManifest.xml) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'aapt manifest check failed.' }
if ($manifestXml -notmatch 'android:allowBackup.*0x0') { throw 'APK must disable Android backup and device transfer.' }

& $zipalign -c -P 16 4 $apkPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'APK is not 16 KiB zip-aligned.' }

$entries = & $aapt list $apkPath
if ($LASTEXITCODE -ne 0) { throw 'Could not list APK entries.' }
$nativeEntries = @($entries | Where-Object { $_ -like 'lib/*' })
if ($nativeEntries.Count -eq 0 -or @($nativeEntries | Where-Object { $_ -notlike 'lib/arm64-v8a/*' }).Count -ne 0) {
    throw 'APK native entries are not ARM64-only.'
}
if (@($entries | Where-Object { $_ -match '^assets/(Data|MODS|SAVE|Content|Base)/' }).Count -ne 0) {
    throw 'APK unexpectedly contains PMDO runtime or user data.'
}
foreach ($requiredNotice in @(
    'assets/Licenses/PROJECT-LICENSE.txt',
    'assets/Licenses/THIRD-PARTY-NOTICES.md',
    'assets/Licenses/DOTNET-THIRD-PARTY-NOTICES.txt')) {
    if ($entries -notcontains $requiredNotice) {
        throw "APK is missing required license material: $requiredNotice"
    }
}

$apksigner = Join-Path $buildTools.FullName 'apksigner.bat'
if (-not (Test-Path -LiteralPath $apksigner)) { throw "apksigner not found at $apksigner" }
$signerStart = [Diagnostics.ProcessStartInfo]::new()
$signerStart.FileName = $apksigner
$signerStart.Arguments = 'verify --print-certs "' + $apkPath.Replace('"', '\"') + '"'
$signerStart.UseShellExecute = $false
$signerStart.CreateNoWindow = $true
$signerStart.RedirectStandardOutput = $true
$signerStart.RedirectStandardError = $true
$signerProcess = [Diagnostics.Process]::Start($signerStart)
$certificateInfo = $signerProcess.StandardOutput.ReadToEnd()
$null = $signerProcess.StandardError.ReadToEnd()
$signerProcess.WaitForExit()
if ($signerProcess.ExitCode -ne 0) { throw 'APK signature verification failed.' }
$expectedCertificate = '81ef0c48e32c988ba8259ca135acbc8c812bd9798b8d6b5f1d3750e119459dfb'
if ($certificateInfo -notmatch "Signer #1 certificate SHA-256 digest: $expectedCertificate") {
    throw 'APK is not signed with the established update certificate.'
}

$ndk = Join-Path $sdkRoot 'ndk\29.0.14206865'
$readelf = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
if (-not (Test-Path $readelf)) { throw "llvm-readelf not found at $readelf" }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pmdo-apk-audit-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($apkPath, $temporary)
    foreach ($library in Get-ChildItem (Join-Path $temporary 'lib\arm64-v8a') -Filter '*.so') {
        $loads = & $readelf -lW $library.FullName | Where-Object { $_ -match '^\s*LOAD\s+' }
        if ($LASTEXITCODE -ne 0 -or @($loads).Count -eq 0) { throw "Could not inspect $($library.Name)." }
        foreach ($line in $loads) {
            $alignToken = ($line.Trim() -split '\s+')[-1]
            $alignment = [Convert]::ToInt64($alignToken.Substring(2), 16)
            if ($alignment -lt 0x4000) { throw "$($library.Name) has LOAD alignment $alignToken." }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

$hash = Get-FileHash $apkPath -Algorithm SHA256
$size = (Get-Item $apkPath).Length
Write-Host "APK audit passed: $apkPath"
Write-Host "Size: $size bytes"
Write-Host "SHA-256: $($hash.Hash)"
