param(
    [string]$OutputRoot = "dist",
    [string]$Version = "",
    [string]$Configuration = "Release",
    [switch]$Installable,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"

function Get-PackageVersion {
    param([string]$PackageJsonPath)

    $package = Get-Content $PackageJsonPath -Raw | ConvertFrom-Json
    return [string]$package.version
}

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
}

function Assert-CommandExists {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Invoke-ExternalStep {
    param(
        [string]$Label,
        [scriptblock]$Command
    )

    Write-Host $Label
    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $Version) {
    $Version = Get-PackageVersion -PackageJsonPath (Join-Path $repoRoot "package.json")
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$releaseName = if ($Installable) {
    "qq-ai-bot-installable-$Version-$timestamp"
}
else {
    "qq-ai-bot-$Version-$timestamp"
}
$outputRootPath = Join-Path $repoRoot $OutputRoot
$releaseDir = Join-Path $outputRootPath $releaseName
$zipPath = "$releaseDir.zip"

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null
Remove-IfExists -Path $releaseDir
Remove-IfExists -Path $zipPath
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$copyItems = @(
    ".env.example",
    ".gitignore",
    "README.md",
    "package.json",
    "package-lock.json",
    "src",
    "desktop",
    "scripts"
)

foreach ($item in $copyItems) {
    $source = Join-Path $repoRoot $item

    if (-not (Test-Path $source)) {
        continue
    }

    Copy-Item $source -Destination (Join-Path $releaseDir $item) -Recurse -Force
}

$cleanupPaths = @(
    "desktop\.testbin",
    "desktop\QQAIBot.Desktop\bin",
    "desktop\QQAIBot.Desktop\obj",
    "node_modules",
    "data",
    "dist",
    "NapCat.Shell.Windows.Node",
    "tmp-backend-out.log",
    "tmp-backend-err.log",
    ".env"
)

foreach ($relativePath in $cleanupPaths) {
    Remove-IfExists -Path (Join-Path $releaseDir $relativePath)
}

if ($Installable) {
    Assert-CommandExists -Name "npm"
    Assert-CommandExists -Name "dotnet"

    Push-Location $releaseDir

    try {
        Invoke-ExternalStep -Label "Installing production Node dependencies into release package." -Command {
            npm ci --omit=dev
        }

        Invoke-ExternalStep -Label "Publishing desktop application into release package." -Command {
            dotnet publish ".\desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj" -c $Configuration -nologo -o ".\desktop-publish"
        }
    }
    finally {
        Pop-Location
    }

    $postPublishCleanupPaths = @(
        "desktop\QQAIBot.Desktop\bin",
        "desktop\QQAIBot.Desktop\obj",
        "desktop\QQAIBot.Desktop.Tests\bin",
        "desktop\QQAIBot.Desktop.Tests\obj"
    )

    foreach ($relativePath in $postPublishCleanupPaths) {
        Remove-IfExists -Path (Join-Path $releaseDir $relativePath)
    }
}

if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath -Force
}

Write-Host "Release directory: $releaseDir"
if (-not $SkipZip) {
    Write-Host "Release archive:   $zipPath"
}
