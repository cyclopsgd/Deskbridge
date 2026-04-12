# Deskbridge smoke test -- launches the app for a few seconds and scans stderr/stdout
# for unhandled exceptions. Intended for CI / quick sanity check before a commit.
#
# Usage:
#   pwsh tools/smoke-test.ps1                 # uses Debug build
#   pwsh tools/smoke-test.ps1 -Config Release # uses Release build
#
# Exit codes:
#   0 = app started, ran for TimeoutSeconds, and exited cleanly with no exceptions logged
#   1 = app failed to start (not built, not found, or crashed immediately)
#   2 = app produced unhandled exceptions or error-level log output
#   3 = app hung (did not exit gracefully after kill)

param(
    [string]$Config = 'Debug',
    [int]$TimeoutSeconds = 5,
    [string]$TargetFramework = 'net10.0-windows'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath  = Join-Path $repoRoot "src/Deskbridge/bin/$Config/$TargetFramework/Deskbridge.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "Deskbridge.exe not found at: $exePath. Run dotnet build first."
    exit 1
}

# Kill any lingering Deskbridge process first
Get-Process -Name Deskbridge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$stdoutPath = [System.IO.Path]::GetTempFileName()
$stderrPath = [System.IO.Path]::GetTempFileName()

Write-Host "Launching: $exePath"
$proc = Start-Process -FilePath $exePath -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -WindowStyle Hidden

$exited = $proc.WaitForExit($TimeoutSeconds * 1000)

if (-not $exited) {
    Write-Host ("Timeout reached ({0}s) -- stopping process {1}" -f $TimeoutSeconds, $proc.Id)
    try {
        # PowerShell 5.1 Process.Kill() has no boolean overload; use taskkill /T for the tree.
        Start-Process -FilePath 'taskkill' -ArgumentList @('/PID', $proc.Id, '/T', '/F') -Wait -NoNewWindow -ErrorAction SilentlyContinue | Out-Null
        $proc.WaitForExit(3000) | Out-Null
    } catch {
        Write-Warning "Failed to kill process: $_"
        exit 3
    }
}

$stdout = Get-Content $stdoutPath -Raw -ErrorAction SilentlyContinue
$stderr = Get-Content $stderrPath -Raw -ErrorAction SilentlyContinue
Remove-Item $stdoutPath, $stderrPath -ErrorAction SilentlyContinue

if ($stderr) {
    Write-Host '--- stderr ---'
    Write-Host $stderr
}
if ($stdout) {
    Write-Host '--- stdout ---'
    Write-Host $stdout
}

# Scan Serilog file sink (app writes to %LOCALAPPDATA%/Deskbridge/logs)
$logDir = Join-Path $env:LOCALAPPDATA 'Deskbridge/logs'
if (Test-Path $logDir) {
    $latestLog = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestLog -and $latestLog.LastWriteTime -gt (Get-Date).AddMinutes(-1)) {
        Write-Host ("--- recent log: {0} ---" -f $latestLog.FullName)
        $errors = Get-Content $latestLog.FullName | Where-Object { $_ -match '\[(ERR|FTL)\]' }
        if ($errors) {
            Write-Host 'Log errors found:'
            $errors | ForEach-Object { Write-Host "  $_" }
            exit 2
        }
    }
}

# Check for common exception patterns in stderr/stdout
$hasException = $false
if ($stderr -match 'Unhandled exception|System\.\w+Exception|at Deskbridge\.') {
    Write-Host 'Unhandled exception detected in stderr.'
    $hasException = $true
}
if ($stdout -match 'Unhandled exception|System\.\w+Exception|at Deskbridge\.') {
    Write-Host 'Unhandled exception detected in stdout.'
    $hasException = $true
}

if ($hasException) { exit 2 }

Write-Host ('Smoke test passed: app ran for {0}s without crashing.' -f $TimeoutSeconds)
exit 0
