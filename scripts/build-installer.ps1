param(
    [string]$PackageRoot = "",
    [string]$OutputRoot = "dist\\installer",
    [string]$Version = "",
    [string]$Configuration = "Release",
    [switch]$SkipPackageBuild,
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

function Resolve-RepoRoot {
    return Resolve-AbsolutePath (Split-Path -Parent $PSScriptRoot)
}

function Get-PackageVersion {
    param([string]$PackageJsonPath)

    $package = Get-Content $PackageJsonPath -Raw | ConvertFrom-Json
    return [string]$package.version
}

function Test-InstallablePackageRoot {
    param([string]$RootPath)

    return (Test-Path (Join-Path $RootPath "package.json")) `
        -and (Test-Path (Join-Path $RootPath "src\index.mjs")) `
        -and (Test-Path (Join-Path $RootPath "node_modules")) `
        -and (Test-Path (Join-Path $RootPath "desktop-publish\QQAIBot.Desktop.exe")) `
        -and (Test-Path (Join-Path $RootPath "scripts\install.ps1"))
}

function Resolve-IsccPath {
    $command = Get-Command iscc -ErrorAction SilentlyContinue

    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
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

$repoRoot = Resolve-RepoRoot
$outputRootPath = Resolve-AbsolutePath $OutputRoot
$installerScriptPath = Join-Path $repoRoot "installer\QQAIBot.iss"
$setupIconPath = Join-Path $repoRoot "installer\assets\qq-ai-bot.ico"

if (-not (Test-Path $installerScriptPath)) {
    throw "Installer script is missing: $installerScriptPath"
}

if (-not (Test-Path $setupIconPath)) {
    throw "Installer icon is missing: $setupIconPath"
}

if (-not $Version) {
    $Version = Get-PackageVersion -PackageJsonPath (Join-Path $repoRoot "package.json")
}

if ($PackageRoot) {
    $packageRootPath = Resolve-AbsolutePath $PackageRoot
}
elseif (-not $SkipPackageBuild) {
    $packageOutputRoot = Join-Path $outputRootPath "package"
    Invoke-ExternalStep -Label "Building installable release package" -Command {
        $releaseScriptPath = Join-Path $repoRoot "scripts\release.ps1"
        $releaseArgs = @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            $releaseScriptPath,
            "-Installable",
            "-OutputRoot",
            $packageOutputRoot,
            "-Configuration",
            $Configuration,
            "-SkipZip"
        )

        & powershell @releaseArgs
    }

    $packageRootPath = Get-ChildItem $packageOutputRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
else {
    throw "PackageRoot is required when -SkipPackageBuild is used."
}

if (-not (Test-InstallablePackageRoot -RootPath $packageRootPath)) {
    throw "Installable package root is missing required files: $packageRootPath"
}

$isccPath = Resolve-IsccPath

Write-Step "Package root: $packageRootPath"
Write-Step "Installer script: $installerScriptPath"

if ($ValidateOnly) {
    if ($isccPath) {
        Write-Step "Found Inno Setup compiler: $isccPath"
    }
    else {
        Write-Step "Inno Setup compiler not found on this machine. Validation only checked package structure and script presence."
    }

    exit 0
}

if (-not $isccPath) {
    throw "ISCC.exe was not found. Install Inno Setup 6 or add iscc to PATH."
}

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputBaseFilename = "qq-ai-bot-setup-$Version-$timestamp"

Invoke-ExternalStep -Label "Building Inno Setup installer" -Command {
    & $isccPath `
        "/DSourceDir=$packageRootPath" `
        "/DAppVersion=$Version" `
        "/DOutputDir=$outputRootPath" `
        "/DOutputBaseFilename=$outputBaseFilename" `
        "/DSetupIconPath=$setupIconPath" `
        $installerScriptPath
}

Write-Host ""
Write-Host "Installer build completed."
Write-Host "Output directory: $outputRootPath"
Write-Host "Output file:      $(Join-Path $outputRootPath "$outputBaseFilename.exe")"
