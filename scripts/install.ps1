param(
    [string]$PackageRoot = "",
    [string]$InstallRoot = "",
    [string]$Configuration = "Release",
    [switch]$SkipNodeInstall,
    [switch]$SkipDesktopPublish,
    [switch]$NoShortcuts,
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

function Resolve-PackageRootPath {
    param([string]$RequestedPackageRoot)

    if ($RequestedPackageRoot) {
        return Resolve-AbsolutePath $RequestedPackageRoot
    }

    return Resolve-AbsolutePath (Split-Path -Parent $PSScriptRoot)
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

function Assert-CommandExists {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Test-BackendPackageRoot {
    param([string]$RootPath)

    return (Test-Path (Join-Path $RootPath "package.json")) `
        -and (Test-Path (Join-Path $RootPath "src\index.mjs")) `
        -and (Test-Path (Join-Path $RootPath "desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj"))
}

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
}

function Get-PackageVersion {
    param([string]$PackageJsonPath)

    $package = Get-Content $PackageJsonPath -Raw | ConvertFrom-Json
    return [string]$package.version
}

function Get-DesktopVersionMetadata {
    param([string]$SemanticVersion)

    $normalizedVersion = if ([string]::IsNullOrWhiteSpace($SemanticVersion)) {
        "1.0.0"
    }
    else {
        $SemanticVersion.Trim()
    }

    $coreVersion = $normalizedVersion.Split('+')[0].Split('-')[0]
    $parts = $coreVersion.Split('.')
    $numericParts = @()

    foreach ($part in $parts) {
        if ($numericParts.Count -ge 4) {
            break
        }

        $parsedValue = 0
        if (-not [int]::TryParse($part, [ref]$parsedValue)) {
            throw "Package version is not compatible with desktop assembly/file version metadata: $normalizedVersion"
        }

        $numericParts += [string]$parsedValue
    }

    while ($numericParts.Count -lt 4) {
        $numericParts += "0"
    }

    return @{
        Version = $normalizedVersion
        InformationalVersion = $normalizedVersion
        AssemblyVersion = ($numericParts[0..3] -join '.')
        FileVersion = ($numericParts[0..3] -join '.')
    }
}

function Invoke-ExternalStep {
    param(
        [string]$Label,
        [scriptblock]$Command
    )

    Write-Step $Label
    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    if (-not (Test-Path $shortcutDirectory)) {
        New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

$packageRootPath = Resolve-PackageRootPath -RequestedPackageRoot $PackageRoot
$installRootPath = Resolve-InstallRootPath -RequestedInstallRoot $InstallRoot
$appRootPath = Join-Path $installRootPath "app"
$bundledNodeModulesPath = Join-Path $packageRootPath "node_modules"
$bundledDesktopPublishPath = Join-Path $packageRootPath "desktop-publish"
$bundledDesktopExePath = Join-Path $bundledDesktopPublishPath "QQAIBot.Desktop.exe"
$desktopPublishPath = Join-Path $appRootPath "desktop-publish"
$desktopProjectPath = Join-Path $appRootPath "desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj"
$desktopExePath = Join-Path $desktopPublishPath "QQAIBot.Desktop.exe"
$installInfoPath = Join-Path $installRootPath "install-info.json"
$envPath = Join-Path $appRootPath ".env"
$dataRootPath = Join-Path $appRootPath "data"
$runtimeConfigPath = Join-Path $dataRootPath "runtime-settings.json"
$sessionStorePath = Join-Path $dataRootPath "sessions.json"
$imageCachePath = Join-Path $dataRootPath "image-cache"
$snapshotRootPath = Join-Path $appRootPath "artifacts\state-snapshots"
$desktopActivityStatePath = Resolve-DesktopActivityStateFilePath -BackendRootPath $appRootPath
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "Local AI Runtime.lnk"
$startMenuShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "Local AI Runtime.lnk"
$legacyDesktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "QQ AI Bot.lnk"
$legacyStartMenuShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "QQ AI Bot.lnk"
$hasBundledNodeModules = Test-Path $bundledNodeModulesPath
$hasBundledDesktopPublish = Test-Path $bundledDesktopExePath
$hadExistingEnv = Test-Path $envPath
$hadExistingData = Test-Path $dataRootPath
$hadExistingSnapshots = Test-Path $snapshotRootPath
$hadExistingDesktopActivityState = Test-Path $desktopActivityStatePath
$packageVersion = Get-PackageVersion -PackageJsonPath (Join-Path $packageRootPath "package.json")
$desktopVersionMetadata = Get-DesktopVersionMetadata -SemanticVersion $packageVersion

Write-Step "Package root: $packageRootPath"
Write-Step "Install root: $installRootPath"

if (-not (Test-BackendPackageRoot -RootPath $packageRootPath)) {
    throw "Package root is missing required backend files: $packageRootPath"
}

Assert-CommandExists -Name "node"

if ($SkipNodeInstall -and -not $hasBundledNodeModules) {
    throw "SkipNodeInstall was requested, but the package does not include node_modules."
}

if ($SkipDesktopPublish -and -not $hasBundledDesktopPublish) {
    throw "SkipDesktopPublish was requested, but the package does not include desktop-publish\QQAIBot.Desktop.exe."
}

if (-not $SkipNodeInstall -and -not $hasBundledNodeModules) {
    Assert-CommandExists -Name "npm"
}

if (-not $SkipDesktopPublish -and -not $hasBundledDesktopPublish) {
    Assert-CommandExists -Name "dotnet"
}

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

New-Item -ItemType Directory -Path $installRootPath -Force | Out-Null
New-Item -ItemType Directory -Path $appRootPath -Force | Out-Null

$replacePaths = @(
    ".env.example",
    ".gitignore",
    "README.md",
    "package.json",
    "package-lock.json",
    "installer",
    "src",
    "desktop",
    "scripts",
    "node_modules",
    "desktop-publish"
)

foreach ($relativePath in $replacePaths) {
    Remove-IfExists -Path (Join-Path $appRootPath $relativePath)
}

$copyItems = @(
    ".env.example",
    ".gitignore",
    "README.md",
    "package.json",
    "package-lock.json",
    "installer",
    "src",
    "desktop",
    "scripts"
)

if ($hasBundledNodeModules) {
    $copyItems += "node_modules"
}

if ($hasBundledDesktopPublish) {
    $copyItems += "desktop-publish"
}

foreach ($item in $copyItems) {
    $sourcePath = Join-Path $packageRootPath $item

    if (-not (Test-Path $sourcePath)) {
        continue
    }

    Write-Step "Copying $item"
    Copy-Item $sourcePath -Destination (Join-Path $appRootPath $item) -Recurse -Force
}

$cleanupPaths = @(
    "desktop\.testbin",
    "desktop\QQAIBot.Desktop\bin",
    "desktop\QQAIBot.Desktop\obj",
    "desktop\QQAIBot.Desktop.Tests\bin",
    "desktop\QQAIBot.Desktop.Tests\obj",
    "dist",
    "NapCat.Shell.Windows.Node",
    "tmp-backend-out.log",
    "tmp-backend-err.log"
)

foreach ($relativePath in $cleanupPaths) {
    Remove-IfExists -Path (Join-Path $appRootPath $relativePath)
}

$createdEnvFromExample = $false
if (-not (Test-Path $envPath)) {
    Write-Step "Creating .env from .env.example"
    Copy-Item (Join-Path $appRootPath ".env.example") -Destination $envPath -Force
    $createdEnvFromExample = $true
}
else {
    Write-Step "Keeping existing .env"
}

Push-Location $appRootPath

try {
    if ($hasBundledNodeModules) {
        Write-Step "Using bundled Node dependencies"
    }
    elseif (-not $SkipNodeInstall) {
        Invoke-ExternalStep -Label "Installing Node dependencies" -Command { npm ci --omit=dev }
    }

    if ($hasBundledDesktopPublish) {
        Write-Step "Using bundled desktop application publish output"
    }
    elseif (-not $SkipDesktopPublish) {
        Invoke-ExternalStep -Label "Publishing desktop application" -Command {
            dotnet publish $desktopProjectPath -c $Configuration -nologo -o $desktopPublishPath `
                "-p:Version=$($desktopVersionMetadata.Version)" `
                "-p:InformationalVersion=$($desktopVersionMetadata.InformationalVersion)" `
                "-p:AssemblyVersion=$($desktopVersionMetadata.AssemblyVersion)" `
                "-p:FileVersion=$($desktopVersionMetadata.FileVersion)"
        }
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $desktopExePath)) {
    throw "Desktop executable was not generated: $desktopExePath"
}

if (-not $NoShortcuts) {
    Write-Step "Removing legacy shortcuts"
    Remove-IfExists -Path $legacyDesktopShortcutPath
    Remove-IfExists -Path $legacyStartMenuShortcutPath

    Write-Step "Creating desktop shortcut"
    New-Shortcut `
        -ShortcutPath $desktopShortcutPath `
        -TargetPath $desktopExePath `
        -WorkingDirectory $appRootPath `
        -Description "Launch Local AI Runtime Console"

    Write-Step "Creating Start Menu shortcut"
    New-Shortcut `
        -ShortcutPath $startMenuShortcutPath `
        -TargetPath $desktopExePath `
        -WorkingDirectory $appRootPath `
        -Description "Launch Local AI Runtime Console"
}

$installInfo = @{
    packageVersion = Get-PackageVersion -PackageJsonPath (Join-Path $appRootPath "package.json")
    installedAt = (Get-Date).ToString("o")
    packageRoot = $packageRootPath
    installRoot = $installRootPath
    appRoot = $appRootPath
    desktopExePath = $desktopExePath
    envPath = $envPath
    bootstrapEnvPath = $envPath
    runtimeConfigPath = $runtimeConfigPath
    dataRootPath = $dataRootPath
    sessionStorePath = $sessionStorePath
    imageCachePath = $imageCachePath
    snapshotRootPath = $snapshotRootPath
    desktopActivityStatePath = $desktopActivityStatePath
    desktopShortcutPath = $desktopShortcutPath
    startMenuShortcutPath = $startMenuShortcutPath
    usedBundledNodeModules = $hasBundledNodeModules
    usedBundledDesktopPublish = $hasBundledDesktopPublish
} | ConvertTo-Json

Set-Content -Path $installInfoPath -Value $installInfo -Encoding UTF8

$configStatusParts = @()
$configStatusParts += if ($createdEnvFromExample) { "created .env from .env.example" } else { "kept existing .env" }
if ($hadExistingData) {
    $configStatusParts += "kept existing data/"
}
if ($hadExistingSnapshots) {
    $configStatusParts += "kept existing state snapshots"
}
if ($hadExistingDesktopActivityState) {
    $configStatusParts += "kept desktop activity history"
}

Write-Host ""
Write-Host "Install completed."
Write-Host "App root:                    $appRootPath"
Write-Host "Desktop binary:              $desktopExePath"
Write-Host "Runtime config file:         $runtimeConfigPath"
Write-Host "Bootstrap env (.env):        $envPath"
Write-Host "Sessions store:              $sessionStorePath"
Write-Host "Image cache:                 $imageCachePath"
Write-Host "State snapshots:             $snapshotRootPath"
Write-Host "Desktop activity state:      $desktopActivityStatePath"
if (-not $NoShortcuts) {
    Write-Host "Desktop shortcut:            $desktopShortcutPath"
    Write-Host "Start menu shortcut:         $startMenuShortcutPath"
}
else {
    Write-Host "Shortcuts:                   skipped (-NoShortcuts)"
}
Write-Host "Config status:               $([string]::Join('; ', $configStatusParts))"
Write-Host ""
Write-Host "Upgrade behavior:"
Write-Host "- Re-run this script with a newer package to upgrade in place."
Write-Host "- Upgrades replace app files under app\ and keep bootstrap .env, data\ (including runtime-settings.json), artifacts\state-snapshots, and the desktop activity state file."
Write-Host "- If this was the first install, fill in .env before starting the runtime; desktop saves later runtime edits into data\runtime-settings.json."
