param(
    [string]$InstallRoot = "",
    [switch]$KeepState,
    [switch]$NoShortcuts,
    [switch]$ForceStop,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message)
}

function Resolve-AbsolutePath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path is required."
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($PathValue)

    if ([System.IO.Path]::IsPathRooted($expanded)) {
        return [System.IO.Path]::GetFullPath($expanded)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $expanded))
}

function Resolve-InstallRootPath {
    param([string]$RequestedInstallRoot)

    if ($RequestedInstallRoot) {
        return Resolve-AbsolutePath $RequestedInstallRoot
    }

    return Resolve-AbsolutePath (Join-Path $env:LOCALAPPDATA "QQAIBot")
}

function Get-DesktopActivityStateStoreRootPath {
    return Join-Path $env:LOCALAPPDATA "QQAIBot.Desktop\activity-state"
}

function Resolve-DesktopActivityStateFilePath {
    param([string]$BackendRootPath)

    $normalizedPath = if ([string]::IsNullOrWhiteSpace($BackendRootPath)) {
        "default"
    }
    else {
        $BackendRootPath.Trim().ToLowerInvariant()
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalizedPath))
    }
    finally {
        $sha256.Dispose()
    }
    $hash = ([System.BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
    return Join-Path (Get-DesktopActivityStateStoreRootPath) "$hash.json"
}

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
}

function Remove-DirectoryIfEmpty {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    $remainingItems = Get-ChildItem -Force $Path

    if ($remainingItems.Count -eq 0) {
        Remove-Item $Path -Force
    }
}

function Remove-ShortcutIfExists {
    param([string]$ShortcutPath)

    if (Test-Path $ShortcutPath) {
        Remove-Item $ShortcutPath -Force
    }
}

function Remove-AutoStartEntry {
    param([string]$ValueName)

    $runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

    if (Test-Path $runKeyPath) {
        Remove-ItemProperty -Path $runKeyPath -Name $ValueName -ErrorAction SilentlyContinue
    }
}

function Get-OwnedDesktopProcesses {
    param([string]$InstallRootPath)

    $ownedProcesses = @()
    $desktopProcesses = Get-Process -Name "QQAIBot.Desktop" -ErrorAction SilentlyContinue

    foreach ($process in $desktopProcesses) {
        $processPath = $null

        try {
            $processPath = $process.Path
        }
        catch {
            $processPath = $null
        }

        if ($processPath -and $processPath.StartsWith($InstallRootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $ownedProcesses += $process
        }
    }

    return $ownedProcesses
}

function Ensure-DesktopProcessesStopped {
    param(
        [string]$InstallRootPath,
        [switch]$ForceStopProcesses
    )

    $ownedProcesses = @(Get-OwnedDesktopProcesses -InstallRootPath $InstallRootPath)

    if ($ownedProcesses.Count -eq 0) {
        return
    }

    if (-not $ForceStopProcesses) {
        $processSummary = ($ownedProcesses | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
        throw "Local AI Runtime desktop is still running: $processSummary. Close it first or rerun uninstall with -ForceStop."
    }

    foreach ($process in $ownedProcesses) {
        Write-Step "Stopping desktop process PID=$($process.Id)"
        Stop-Process -Id $process.Id -Force
    }

    Start-Sleep -Milliseconds 500
}

$installRootPath = Resolve-InstallRootPath -RequestedInstallRoot $InstallRoot
$appRootPath = Join-Path $installRootPath "app"
$envPath = Join-Path $appRootPath ".env"
$dataPath = Join-Path $appRootPath "data"
$runtimeConfigPath = Join-Path $dataPath "runtime-settings.json"
$sessionStorePath = Join-Path $dataPath "sessions.json"
$imageCachePath = Join-Path $dataPath "image-cache"
$snapshotRootPath = Join-Path $appRootPath "artifacts\state-snapshots"
$desktopActivityStatePath = Resolve-DesktopActivityStateFilePath -BackendRootPath $appRootPath
$installInfoPath = Join-Path $installRootPath "install-info.json"
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "Local AI Runtime.lnk"
$startMenuShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "Local AI Runtime.lnk"
$legacyDesktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "QQ AI Bot.lnk"
$legacyStartMenuShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "QQ AI Bot.lnk"

Write-Step "Install root: $installRootPath"

if ($ValidateOnly) {
    Write-Step "Validation succeeded."
    Write-Host "Expected app root:              $appRootPath"
    Write-Host "Expected runtime config file:   $runtimeConfigPath"
    Write-Host "Expected bootstrap env (.env):  $envPath"
    Write-Host "Expected sessions store:        $sessionStorePath"
    Write-Host "Expected image cache:           $imageCachePath"
    Write-Host "Expected state snapshots:       $snapshotRootPath"
    Write-Host "Expected desktop activity state:$desktopActivityStatePath"
    exit 0
}

Ensure-DesktopProcessesStopped -InstallRootPath $installRootPath -ForceStopProcesses:$ForceStop

if (-not $NoShortcuts) {
    Write-Step "Removing shortcuts"
    Remove-ShortcutIfExists -ShortcutPath $desktopShortcutPath
    Remove-ShortcutIfExists -ShortcutPath $startMenuShortcutPath
    Remove-ShortcutIfExists -ShortcutPath $legacyDesktopShortcutPath
    Remove-ShortcutIfExists -ShortcutPath $legacyStartMenuShortcutPath
}

Write-Step "Removing current-user auto-start entry"
Remove-AutoStartEntry -ValueName "QQAIBot.Desktop"

if (-not (Test-Path $installRootPath)) {
    Write-Host ""
    Write-Host "Nothing to uninstall. Install root does not exist."
    exit 0
}

if ($KeepState) {
    Write-Step "Removing installed application files and keeping bootstrap .env/data/snapshots"

    if (Test-Path $appRootPath) {
        $preservedNames = @(".env", "data", "artifacts")

        Get-ChildItem -Force $appRootPath |
            Where-Object { $preservedNames -notcontains $_.Name } |
            ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

        if (-not (Test-Path $envPath) -and -not (Test-Path $dataPath) -and -not (Test-Path $snapshotRootPath)) {
            Remove-IfExists -Path $appRootPath
        }
    }

    Remove-IfExists -Path $installInfoPath
    Remove-DirectoryIfEmpty -Path $installRootPath

    Write-Host ""
    Write-Host "Uninstall completed."
    $preservedStateLines = @()
    if (Test-Path $runtimeConfigPath) {
        $preservedStateLines += "Runtime config file:         $runtimeConfigPath"
    }
    if (Test-Path $envPath) {
        $preservedStateLines += "Bootstrap env (.env):        $envPath"
    }
    if (Test-Path $sessionStorePath) {
        $preservedStateLines += "Sessions store:              $sessionStorePath"
    }
    if (Test-Path $imageCachePath) {
        $preservedStateLines += "Image cache:                 $imageCachePath"
    }
    if (Test-Path $snapshotRootPath) {
        $preservedStateLines += "State snapshots:             $snapshotRootPath"
    }
    if (Test-Path $desktopActivityStatePath) {
        $preservedStateLines += "Desktop activity state:      $desktopActivityStatePath"
    }

    if ($preservedStateLines.Count -gt 0) {
        Write-Host "Preserved state:"
        foreach ($line in $preservedStateLines) {
            Write-Host $line
        }
        Write-Host "Reinstall with npm run setup:install to attach to the same preserved state."
    }
    else {
        Write-Host "No preserved state was found."
    }
    exit 0
}

Write-Step "Removing installed application files"
Remove-IfExists -Path $installInfoPath
Remove-IfExists -Path $appRootPath
Write-Step "Removing desktop activity state for this install"
Remove-IfExists -Path $desktopActivityStatePath
Remove-DirectoryIfEmpty -Path (Split-Path -Parent $desktopActivityStatePath)
Remove-DirectoryIfEmpty -Path (Split-Path -Parent (Get-DesktopActivityStateStoreRootPath))
Remove-DirectoryIfEmpty -Path $installRootPath

Write-Host ""
Write-Host "Uninstall completed."
Write-Host "Removed runtime config, bootstrap .env, data, state snapshots, and desktop activity state for this install."
if (Test-Path $installRootPath) {
    Write-Host "Install root still exists because it contains extra files: $installRootPath"
}
else {
    Write-Host "Install root removed: $installRootPath"
}
