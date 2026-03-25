param(
    [string]$WorkspacePath = "",
    [int]$TimeoutSeconds = 30,
    [switch]$AutomatedOnly,
    [switch]$SkipBuild,
    [switch]$SkipNodeTests,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message)
}

function Wait-ForCondition {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutSeconds = 30,
        [string]$Label = "condition"
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            return
        }

        Start-Sleep -Milliseconds 200
    }

    throw "Timed out waiting for $Label."
}

function Resolve-RepoRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Resolve-WorkspacePath {
    param([string]$RepoRoot, [string]$RequestedWorkspacePath)

    if ($RequestedWorkspacePath) {
        return (Resolve-Path $RequestedWorkspacePath).Path
    }

    return $RepoRoot
}

function Resolve-DesktopExePath {
    param([string]$RepoRoot)

    $candidate = Join-Path $RepoRoot "desktop\QQAIBot.Desktop\bin\Debug\net8.0-windows\QQAIBot.Desktop.exe"

    if (-not (Test-Path $candidate)) {
        throw "Desktop executable not found: $candidate"
    }

    return $candidate
}

function Start-DesktopProcess {
    param(
        [string]$DesktopExePath,
        [string]$WorkingDirectory,
        [string[]]$Arguments,
        [string]$SignalFilePath,
        [string]$ScopeSuffix
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $DesktopExePath
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.Arguments = ($Arguments -join " ")
    $psi.Environment["QQ_AI_BOT_DESKTOP_TEST_SIGNAL_FILE"] = $SignalFilePath
    $psi.Environment["QQ_AI_BOT_DESKTOP_SINGLE_INSTANCE_SUFFIX"] = $ScopeSuffix

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    if (-not $process.Start()) {
        throw "Failed to start desktop process."
    }

    return $process
}

function Wait-ForSignal {
    param(
        [string]$SignalFilePath,
        [string]$SignalName,
        [int]$TimeoutSeconds = 30
    )

    Wait-ForCondition -TimeoutSeconds $TimeoutSeconds -Label "signal '$SignalName'" -Condition {
        if (-not (Test-Path $SignalFilePath)) {
            return $false
        }

        $content = Get-Content $SignalFilePath -Raw
        return $content.Contains($SignalName)
    }
}

function Wait-ForProcessExit {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 30,
        [string]$Label = "process exit"
    )

    Wait-ForCondition -TimeoutSeconds $TimeoutSeconds -Label $Label -Condition {
        return $Process.HasExited
    }
}

function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            $Process.Kill($true)
            $Process.WaitForExit()
        }
    } catch {
    } finally {
        $Process.Dispose()
    }
}

$repoRoot = Resolve-RepoRoot
$workspace = Resolve-WorkspacePath -RepoRoot $repoRoot -RequestedWorkspacePath $WorkspacePath
$scopeSuffix = [guid]::NewGuid().ToString("N")
$signalFilePath = Join-Path $env:TEMP ("qq-ai-bot-desktop-acceptance-{0}.log" -f $scopeSuffix)

Write-Step "Repo root: $repoRoot"
Write-Step "Workspace: $workspace"
Write-Step "Signal file: $signalFilePath"

if ($ValidateOnly) {
    if (-not (Test-Path (Join-Path $workspace "package.json"))) {
        throw "Workspace is missing package.json: $workspace"
    }

    if (-not (Test-Path (Join-Path $workspace "src\index.mjs"))) {
        throw "Workspace is missing src\\index.mjs: $workspace"
    }

    if (-not (Test-Path (Join-Path $repoRoot "desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj"))) {
        throw "Desktop project is missing."
    }

    Write-Step "Validation succeeded. No processes were launched."
    exit 0
}

Push-Location $repoRoot

$primary = $null
$activate = $null
$ensureRuntime = $null

try {
    if (-not $SkipNodeTests) {
        Write-Step "Running automated regression suite."
        npm test
    }

    if (-not $SkipBuild) {
        Write-Step "Building desktop project."
        dotnet build ".\desktop\QQAIBot.Desktop\QQAIBot.Desktop.csproj" -nologo
    }

    $desktopExePath = Resolve-DesktopExePath -RepoRoot $repoRoot

    if (Test-Path $signalFilePath) {
        Remove-Item $signalFilePath -Force
    }

    Write-Step "Launching primary desktop instance."
    $primary = Start-DesktopProcess `
        -DesktopExePath $desktopExePath `
        -WorkingDirectory $workspace `
        -Arguments @() `
        -SignalFilePath $signalFilePath `
        -ScopeSuffix $scopeSuffix
    Wait-ForSignal -SignalFilePath $signalFilePath -SignalName "window-loaded" -TimeoutSeconds $TimeoutSeconds
    Write-Step "Primary instance loaded."

    Write-Step "Launching secondary desktop instance for restore signal."
    $activate = Start-DesktopProcess `
        -DesktopExePath $desktopExePath `
        -WorkingDirectory $workspace `
        -Arguments @() `
        -SignalFilePath $signalFilePath `
        -ScopeSuffix $scopeSuffix
    Wait-ForProcessExit -Process $activate -TimeoutSeconds $TimeoutSeconds -Label "secondary desktop activation exit"
    Wait-ForSignal -SignalFilePath $signalFilePath -SignalName "restore-from-external-activation" -TimeoutSeconds $TimeoutSeconds
    Write-Step "Restore signal observed."

    Write-Step "Launching ensure-runtime desktop instance."
    $ensureRuntime = Start-DesktopProcess `
        -DesktopExePath $desktopExePath `
        -WorkingDirectory $workspace `
        -Arguments @("--ensure-runtime") `
        -SignalFilePath $signalFilePath `
        -ScopeSuffix $scopeSuffix
    Wait-ForProcessExit -Process $ensureRuntime -TimeoutSeconds $TimeoutSeconds -Label "desktop ensure-runtime exit"
    Wait-ForSignal -SignalFilePath $signalFilePath -SignalName "ensure-runtime-from-external-activation" -TimeoutSeconds $TimeoutSeconds
    Write-Step "Ensure-runtime signal observed."

    if (-not $AutomatedOnly) {
        Write-Host ""
        Write-Host "Manual acceptance checklist:"
        Write-Host "1. Minimize the desktop window. Confirm it hides to tray and a tray balloon appears."
        Write-Host "2. Open the tray menu. Confirm menu items are Open / Start Backend / Stop Backend / Exit."
        Write-Host "3. Use tray menu Open. Confirm the window restores."
        Write-Host "4. Close the window. Confirm it hides to tray and shows the tray balloon."
        Write-Host "5. Use tray menu Exit to close the application."
        Write-Host ""
        Write-Host "Signal log:"
        Get-Content $signalFilePath
    }

    Write-Step "Desktop acceptance script completed."
}
finally {
    Stop-ProcessTree -Process $activate
    Stop-ProcessTree -Process $ensureRuntime
    Stop-ProcessTree -Process $primary
    Pop-Location
}
