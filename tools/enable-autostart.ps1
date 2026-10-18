param(
    [string]$ExePath,
    [switch]$Disable
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) { $ExePath = Join-Path $root 'dist\app\PeripheralBattery.exe' }
$ExePath = [System.IO.Path]::GetFullPath($ExePath)

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$name = 'PeripheralBattery'

if ($Disable) {
    Remove-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $approvedKey -Name $name -ErrorAction SilentlyContinue
    Write-Host "Disabled autostart for $name."
    exit 0
}

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath"
}

$command = '"' + $ExePath + '" --tray'

try {
    Set-ItemProperty -Path $runKey -Name $name -Value $command -Type String
    # Remove the Task Manager "disabled" flag so Windows actually launches it.
    Remove-ItemProperty -Path $approvedKey -Name $name -ErrorAction SilentlyContinue
}
catch {
    Write-Host "Failed to write HKCU Run: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "This is usually caused by security software / group policy blocking Run keys."
    Write-Host "Try running this script in a normal (non-sandboxed) PowerShell window as your own user."
    Write-Host "Alternative: create a Task Scheduler task at logon for:"
    Write-Host "  $command"
    exit 1
}

$value = (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue).$name
if ($value -ne $command) {
    Write-Host "Verification failed. Current value: [$value]" -ForegroundColor Red
    exit 1
}

Write-Host "Autostart enabled:" -ForegroundColor Green
Write-Host "  $value"
Write-Host ""
Write-Host "Also make sure Task Manager -> Startup apps has $name enabled."
