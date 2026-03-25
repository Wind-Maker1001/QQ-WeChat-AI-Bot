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
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "QQ AI Bot.lnk"
$startMenuShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "QQ AI Bot.lnk"
$hasBundledNodeModules = Test-Path $bundledNodeModulesPath
$hasBundledDesktopPublish = Test-Path $bundledDesktopExePath
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

$envPath = Join-Path $appRootPath ".env"
if (-not (Test-Path $envPath)) {
    Write-Step "Creating .env from .env.example"
    Copy-Item (Join-Path $appRootPath ".env.example") -Destination $envPath -Force
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
    Write-Step "Creating desktop shortcut"
    New-Shortcut `
        -ShortcutPath $desktopShortcutPath `
        -TargetPath $desktopExePath `
        -WorkingDirectory $appRootPath `
        -Description "Launch QQ AI Bot desktop console"

    Write-Step "Creating Start Menu shortcut"
    New-Shortcut `
        -ShortcutPath $startMenuShortcutPath `
        -TargetPath $desktopExePath `
        -WorkingDirectory $appRootPath `
        -Description "Launch QQ AI Bot desktop console"
}

$installInfo = @{
    packageVersion = Get-PackageVersion -PackageJsonPath (Join-Path $appRootPath "package.json")
    installedAt = (Get-Date).ToString("o")
    packageRoot = $packageRootPath
    installRoot = $installRootPath
    appRoot = $appRootPath
    desktopExePath = $desktopExePath
    usedBundledNodeModules = $hasBundledNodeModules
    usedBundledDesktopPublish = $hasBundledDesktopPublish
} | ConvertTo-Json

Set-Content -Path $installInfoPath -Value $installInfo -Encoding UTF8

Write-Host ""
Write-Host "Install completed."
Write-Host "App root:        $appRootPath"
Write-Host "Desktop binary:  $desktopExePath"
Write-Host "Config file:     $envPath"
Write-Host ""
Write-Host "Re-run this script with a newer package to upgrade in place."
