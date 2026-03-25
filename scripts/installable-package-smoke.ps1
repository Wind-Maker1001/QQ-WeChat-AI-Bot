param(
    [string]$PackageRoot = "",
    [string]$Configuration = "Release",
    [switch]$SkipPackageBuild
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

function Resolve-RepoRoot {
    return Resolve-AbsolutePath (Split-Path -Parent $PSScriptRoot)
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-PowershellFile {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    $commandArgs = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $FilePath
    ) + $Arguments

    & powershell @commandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath"
    }
}

function Test-InstallablePackageRoot {
    param([string]$RootPath)

    return (Test-Path (Join-Path $RootPath "package.json")) `
        -and (Test-Path (Join-Path $RootPath "src\index.mjs")) `
        -and (Test-Path (Join-Path $RootPath "node_modules")) `
        -and (Test-Path (Join-Path $RootPath "desktop-publish\QQAIBot.Desktop.exe")) `
        -and (Test-Path (Join-Path $RootPath "scripts\install.ps1")) `
        -and (Test-Path (Join-Path $RootPath "scripts\uninstall.ps1"))
}

$repoRoot = Resolve-RepoRoot
$installRootPath = Join-Path $env:TEMP ("qq-ai-bot-installable-smoke-install-" + [guid]::NewGuid().ToString("N"))
$packageOutputRoot = Join-Path $env:TEMP ("qq-ai-bot-installable-smoke-package-" + [guid]::NewGuid().ToString("N"))
$builtPackageRootPath = $null

if ($PackageRoot) {
    $packageRootPath = Resolve-AbsolutePath $PackageRoot
}
elseif (-not $SkipPackageBuild) {
    $releaseScriptPath = Join-Path $repoRoot "scripts\release.ps1"
    Write-Step "Building installable package for smoke test"
    Invoke-PowershellFile -FilePath $releaseScriptPath -Arguments @(
        "-Installable",
        "-OutputRoot",
        $packageOutputRoot,
        "-Configuration",
        $Configuration,
        "-SkipZip"
    )

    $builtPackageRootPath = Get-ChildItem $packageOutputRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    $packageRootPath = $builtPackageRootPath
}
else {
    throw "PackageRoot is required when -SkipPackageBuild is used."
}

Assert-True (Test-InstallablePackageRoot -RootPath $packageRootPath) "Installable package root is missing required files: $packageRootPath"

$installScriptPath = Join-Path $packageRootPath "scripts\install.ps1"
$uninstallScriptPath = Join-Path $packageRootPath "scripts\uninstall.ps1"
$installInfoPath = Join-Path $installRootPath "install-info.json"
$envPath = Join-Path $installRootPath "app\.env"
$dataPath = Join-Path $installRootPath "app\data"
$sessionPath = Join-Path $dataPath "sessions.json"
$appRootPath = Join-Path $installRootPath "app"

try {
    Write-Step "Installing from bundled package assets"
    Invoke-PowershellFile -FilePath $installScriptPath -Arguments @(
        "-PackageRoot",
        $packageRootPath,
        "-InstallRoot",
        $installRootPath,
        "-NoShortcuts",
        "-SkipNodeInstall",
        "-SkipDesktopPublish"
    )

    Assert-True (Test-Path $installInfoPath) "Install info was not created: $installInfoPath"
    $installInfo = Get-Content $installInfoPath -Raw | ConvertFrom-Json
    Assert-True ($installInfo.usedBundledNodeModules -eq $true) "Install did not record bundled node_modules usage."
    Assert-True ($installInfo.usedBundledDesktopPublish -eq $true) "Install did not record bundled desktop publish usage."
    Assert-True (Test-Path (Join-Path $installRootPath "app\desktop-publish\QQAIBot.Desktop.exe")) "Desktop executable missing after install."

    Write-Step "Creating simulated retained state"
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    Add-Content -Path $envPath -Value "`nBOT_PERSONA=smoke-test"
    Set-Content -Path $sessionPath -Value '{"version":2}' -Encoding UTF8

    Write-Step "Running keep-state uninstall"
    Invoke-PowershellFile -FilePath $uninstallScriptPath -Arguments @(
        "-InstallRoot",
        $installRootPath,
        "-KeepState",
        "-NoShortcuts"
    )

    Assert-True (Test-Path $envPath) ".env should be preserved by keep-state uninstall."
    Assert-True (Test-Path $sessionPath) "data should be preserved by keep-state uninstall."
    Assert-True (-not (Test-Path (Join-Path $appRootPath "src"))) "App sources should be removed by keep-state uninstall."
    Assert-True (-not (Test-Path $installInfoPath)) "install-info.json should be removed by keep-state uninstall."

    Write-Step "Running full uninstall"
    Invoke-PowershellFile -FilePath $uninstallScriptPath -Arguments @(
        "-InstallRoot",
        $installRootPath,
        "-NoShortcuts"
    )

    Assert-True (-not (Test-Path $installRootPath)) "Install root should be removed by full uninstall."

    Write-Host ""
    Write-Host "Installable package smoke test passed."
}
finally {
    if (Test-Path $installRootPath) {
        Remove-Item $installRootPath -Recurse -Force
    }

    if ($builtPackageRootPath -and (Test-Path $packageOutputRoot)) {
        Remove-Item $packageOutputRoot -Recurse -Force
    }
}
