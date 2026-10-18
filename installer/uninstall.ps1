#requires -Version 5.1
<#
.SYNOPSIS
    卸载「2.4G 外设电量监控」。

.DESCRIPTION
    删除开始菜单/桌面快捷方式、HKCU 开机启动项、程序安装目录。
    默认保留 %APPDATA%\PeripheralBattery（配置与日志）；加 -Purge 一并删除。

.PARAMETER Purge
    同时删除用户配置与日志目录 %APPDATA%\PeripheralBattery。

.PARAMETER InstallDir
    自定义安装目录（与 install.ps1 的 -InstallDir 对应），默认 %LOCALAPPDATA%\Programs\PeripheralBattery。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\uninstall.ps1 -WhatIf

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\uninstall.ps1 -Purge
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Purge,
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'

$runValueName = 'PeripheralBattery'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$dataDir = Join-Path $env:APPDATA 'PeripheralBattery'

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\PeripheralBattery'
}

# 安全检查：只允许删除「看起来像安装目录」的路径，避免手滑传入 C:\ 之类
$resolved = $InstallDir.TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($resolved)) {
    throw 'InstallDir 为空，拒绝执行。'
}
if ($resolved -match '^[A-Za-z]:$' -or -not (Test-Path -LiteralPath $resolved)) {
    throw ('InstallDir 不是有效目录（或盘符根目录，拒绝执行）: ' + $resolved)
}
$leaf = Split-Path -Leaf $resolved
if ($leaf -ne 'PeripheralBattery') {
    Write-Warning ('安装目录末级目录名不是 PeripheralBattery（' + $leaf + '），请确认路径无误。')
}

Write-Host '== 2.4G 外设电量监控 · 卸载 ==' -ForegroundColor Cyan
Write-Host ('安装目录: ' + $resolved)

# 0) 结束进程
$running = @(Get-Process -Name 'PeripheralBattery' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    if ($PSCmdlet.ShouldProcess('PeripheralBattery.exe', '结束正在运行的实例')) {
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
        Write-Host '  已结束正在运行的实例' -ForegroundColor DarkYellow
    }
}

# 1) 快捷方式
$startMenuLnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PeripheralBattery.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PeripheralBattery.lnk'
foreach ($lnk in @($startMenuLnk, $desktopLnk)) {
    if ($lnk -and (Test-Path -LiteralPath $lnk)) {
        if ($PSCmdlet.ShouldProcess($lnk, '删除快捷方式')) {
            Remove-Item -LiteralPath $lnk -Force
            Write-Host ('  已删除快捷方式: ' + $lnk) -ForegroundColor Green
        }
    }
}

# 2) 开机启动项
if (Test-Path -LiteralPath $runKeyPath) {
    $existing = Get-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        if ($PSCmdlet.ShouldProcess(($runKeyPath + ' :: ' + $runValueName), '删除开机启动项')) {
            Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
            Write-Host '  已删除开机启动项' -ForegroundColor Green
        }
    }
}

# 3) 程序目录
if (Test-Path -LiteralPath $resolved) {
    if ($PSCmdlet.ShouldProcess($resolved, '删除程序目录')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Host '  已删除程序目录' -ForegroundColor Green
    }
}

# 4) 配置与日志
if ($Purge) {
    if (Test-Path -LiteralPath $dataDir) {
        if ($PSCmdlet.ShouldProcess($dataDir, '删除用户配置与日志')) {
            Remove-Item -LiteralPath $dataDir -Recurse -Force
            Write-Host '  已删除配置与日志' -ForegroundColor Green
        }
    }
} else {
    Write-Host ('  保留配置与日志: ' + $dataDir + '（如需删除请加 -Purge）')
}

Write-Host ''
Write-Host '卸载完成。' -ForegroundColor Cyan
