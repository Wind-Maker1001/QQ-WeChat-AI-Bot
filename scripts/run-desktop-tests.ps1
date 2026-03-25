param(
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = if ($OutputRoot) {
    Join-Path $repoRoot $OutputRoot
} else {
    Join-Path $env:TEMP "qq-ai-bot-desktop-tests"
}
$testProjectPath = Join-Path $repoRoot "desktop\QQAIBot.Desktop.Tests\QQAIBot.Desktop.Tests.csproj"
$testDllPath = Join-Path $outputPath "QQAIBot.Desktop.Tests.dll"

Remove-IfExists -Path $outputPath
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

& dotnet build `
  $testProjectPath `
  "-nologo" `
  "-p:OutDir=$outputPath\" `
  "-p:BaseIntermediateOutputPath=$outputPath\obj\"

if (-not (Test-Path $testDllPath)) {
    throw "Desktop test harness not found: $testDllPath"
}

& dotnet exec $testDllPath
