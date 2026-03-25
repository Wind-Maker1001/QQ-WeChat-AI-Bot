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
        throw "QQ AI Bot desktop is still running: $processSummary. Close it first or rerun uninstall with -ForceStop."
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
$installInfoPath = Join-Path $installRootPath "install-info.json"
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "QQ AI Bot.lnk"
$startMenuShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "QQ AI Bot.lnk"

Write-Step "Install root: $installRootPath"

if ($ValidateOnly) {
    Write-Step "Validation succeeded."
    exit 0
}

Ensure-DesktopProcessesStopped -InstallRootPath $installRootPath -ForceStopProcesses:$ForceStop

if (-not $NoShortcuts) {
    Write-Step "Removing shortcuts"
    Remove-ShortcutIfExists -ShortcutPath $desktopShortcutPath
    Remove-ShortcutIfExists -ShortcutPath $startMenuShortcutPath
}

Write-Step "Removing current-user auto-start entry"
Remove-AutoStartEntry -ValueName "QQAIBot.Desktop"

if (-not (Test-Path $installRootPath)) {
    Write-Host ""
    Write-Host "Nothing to uninstall. Install root does not exist."
    exit 0
}

if ($KeepState) {
    Write-Step "Removing installed application files and keeping .env/data"

    if (Test-Path $appRootPath) {
        $preservedNames = @(".env", "data")

        Get-ChildItem -Force $appRootPath |
            Where-Object { $preservedNames -notcontains $_.Name } |
            ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

        if (-not (Test-Path $envPath) -and -not (Test-Path $dataPath)) {
            Remove-IfExists -Path $appRootPath
        }
    }

    Remove-IfExists -Path $installInfoPath
    Remove-DirectoryIfEmpty -Path $installRootPath

    Write-Host ""
    Write-Host "Uninstall completed."
    if ((Test-Path $envPath) -or (Test-Path $dataPath)) {
        Write-Host "Preserved state under: $appRootPath"
    }
    else {
        Write-Host "No preserved state was found."
    }
    exit 0
}

Write-Step "Removing installed application files"
Remove-IfExists -Path $installInfoPath
Remove-IfExists -Path $appRootPath
Remove-DirectoryIfEmpty -Path $installRootPath

Write-Host ""
Write-Host "Uninstall completed."
if (Test-Path $installRootPath) {
    Write-Host "Install root still exists because it contains extra files: $installRootPath"
}
else {
    Write-Host "Install root removed: $installRootPath"
}
