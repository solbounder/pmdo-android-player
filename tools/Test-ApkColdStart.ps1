[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Apk,
    [string]$PreviousApk,
    [string]$Serial,
    [switch]$AllowAppDataReset
)

$ErrorActionPreference = 'Stop'
if (-not $AllowAppDataReset) {
    throw 'This test removes the PMDO Android Player app data. Re-run with -AllowAppDataReset on a disposable test device.'
}

$sdkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$adb = Join-Path $sdkRoot 'platform-tools\adb.exe'
$buildTools = Get-ChildItem (Join-Path $sdkRoot 'build-tools') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'aapt.exe') } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if (-not (Test-Path -LiteralPath $adb) -or $null -eq $buildTools) {
    throw 'Android SDK platform tools and build tools are required.'
}

$apkPath = (Resolve-Path -LiteralPath $Apk).Path
$previousApkPath = if ([string]::IsNullOrWhiteSpace($PreviousApk)) {
    $null
}
else {
    (Resolve-Path -LiteralPath $PreviousApk).Path
}
$aapt = Join-Path $buildTools.FullName 'aapt.exe'
$badging = (& $aapt dump badging $apkPath) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the final APK.' }
$packageMatch = [regex]::Match($badging, "package: name='(?<value>[^']+)'")
$activityMatch = [regex]::Match($badging, "launchable-activity: name='(?<value>[^']+)'")
if (-not $packageMatch.Success -or -not $activityMatch.Success) {
    throw 'Could not determine the APK package and launcher activity.'
}
$packageName = $packageMatch.Groups['value'].Value
$component = "$packageName/$($activityMatch.Groups['value'].Value)"
$adbArgs = if ([string]::IsNullOrWhiteSpace($Serial)) { @() } else { @('-s', $Serial) }

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & $adb @adbArgs @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "adb failed: $($Arguments -join ' ')"
    }
}

function Assert-ColdStart {
    param([string]$Label)
    Invoke-Adb -Arguments @('logcat', '-c')
    Invoke-Adb -Arguments @('shell', 'am', 'force-stop', $packageName)
    Invoke-Adb -Arguments @('shell', 'am', 'start', '-W', '-n', $component)
    Start-Sleep -Seconds 4
    $pidValue = (& $adb @adbArgs shell pidof $packageName).Trim()
    $crashLog = (& $adb @adbArgs logcat -d -v brief 'AndroidRuntime:E' 'DOTNET:E' '*:S') -join "`n"
    if ([string]::IsNullOrWhiteSpace($pidValue) -or
        $crashLog -match 'FATAL EXCEPTION|UnsatisfiedLinkError') {
        throw "$Label failed to remain active.`n$crashLog"
    }
    Write-Host "$Label passed with PID $pidValue."
}

function Reset-TestInstall {
    $uninstallOutput = (& $adb @adbArgs uninstall $packageName 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -and $uninstallOutput -notmatch 'Unknown package|not installed') {
        throw "Could not reset the test installation.`n$uninstallOutput"
    }
}

Reset-TestInstall
Invoke-Adb -Arguments @('install', '--no-incremental', $apkPath)
Assert-ColdStart 'Fresh APK cold start'

if ($null -ne $previousApkPath) {
    Reset-TestInstall
    Invoke-Adb -Arguments @('install', '--no-incremental', $previousApkPath)
    Invoke-Adb -Arguments @('install', '--no-incremental', '-r', $apkPath)
    Assert-ColdStart 'In-place update cold start'
}
