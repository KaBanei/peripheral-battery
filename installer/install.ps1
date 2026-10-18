#requires -Version 5.1
<#
.SYNOPSIS
    安装「2.4G 外设电量监控」到当前用户目录（不需要管理员权限）。

.DESCRIPTION
    1. 把 dist\app 下的程序文件复制到 %LOCALAPPDATA%\Programs\PeripheralBattery；
    2. 创建开始菜单快捷方式（可选附带桌面快捷方式）；
    3. 写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 的 PeripheralBattery 值，
       值为 "<exe 完整路径>" --tray（登录后静默启动到托盘）。
    配置与日志不在安装目录，卸载时默认保留。

.PARAMETER SourceDir
    构建输出目录，默认 <仓库>\dist\app。

.PARAMETER InstallDir
    自定义安装目录（主要供自动化测试使用），默认 %LOCALAPPDATA%\Programs\PeripheralBattery。

.PARAMETER DesktopShortcut
    额外创建桌面快捷方式。

.PARAMETER NoShortcut
    跳过所有快捷方式（测试/绿色使用）。

.PARAMETER NoAutoStart
    不写开机启动项（之后可在程序设置里开启）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\install.ps1 -WhatIf
    预览将要做的事，不做任何修改。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\install.ps1 -DesktopShortcut
    正常安装并创建桌面快捷方式。
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceDir,
    [string]$InstallDir,
    [switch]$DesktopShortcut,
    [switch]$NoShortcut,
    [switch]$NoAutoStart
)

$ErrorActionPreference = 'Stop'

$exeName = 'PeripheralBattery.exe'
$runValueName = 'PeripheralBattery'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\PeripheralBattery'
}
if (-not $SourceDir) {
    $SourceDir = Join-Path $repoRoot 'dist\app'
}

Write-Host '== 2.4G 外设电量监控 · 安装 ==' -ForegroundColor Cyan
Write-Host ('源目录:   ' + $SourceDir)
Write-Host ('安装目录: ' + $InstallDir)

$sourceExe = Join-Path $SourceDir $exeName
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw ('找不到 ' + $sourceExe + '。请先运行 build.ps1 构建，或用 -SourceDir 指定 dist\app 的位置。')
}
$exePath = Join-Path $InstallDir $exeName

# 0) 结束正在运行的实例，避免 exe 被占用
$running = @(Get-Process -Name 'PeripheralBattery' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    if ($PSCmdlet.ShouldProcess('PeripheralBattery.exe', '结束正在运行的实例')) {
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
        Write-Host '  已结束正在运行的实例' -ForegroundColor DarkYellow
    }
}

# 1) 复制程序文件
if ($PSCmdlet.ShouldProcess($InstallDir, '复制程序文件')) {
    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }
    Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force
    Write-Host '  已复制程序文件' -ForegroundColor Green
}

# 2) 快捷方式（WScript.Shell，路径全部加引号由 COM 负责）
function New-AppShortcut {
    param(
        [string]$LinkPath,
        [string]$TargetExe,
        [string]$WorkDir
    )
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($LinkPath)
    $shortcut.TargetPath = $TargetExe
    $shortcut.WorkingDirectory = $WorkDir
    $shortcut.Description = '2.4G 外设电量监控'
    $shortcut.IconLocation = ($TargetExe + ',0')
    $shortcut.Save()
}

if (-not $NoShortcut) {
    $startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $startMenuLnk = Join-Path $startMenuDir 'PeripheralBattery.lnk'
    if ($PSCmdlet.ShouldProcess($startMenuLnk, '创建开始菜单快捷方式')) {
        if (-not (Test-Path -LiteralPath $startMenuDir)) {
            New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
        }
        New-AppShortcut -LinkPath $startMenuLnk -TargetExe $exePath -WorkDir $InstallDir
        Write-Host '  已创建开始菜单快捷方式' -ForegroundColor Green
    }

    if ($DesktopShortcut) {
        $desktop = [Environment]::GetFolderPath('Desktop')
        if (-not [string]::IsNullOrEmpty($desktop)) {
            $desktopLnk = Join-Path $desktop 'PeripheralBattery.lnk'
            if ($PSCmdlet.ShouldProcess($desktopLnk, '创建桌面快捷方式')) {
                New-AppShortcut -LinkPath $desktopLnk -TargetExe $exePath -WorkDir $InstallDir
                Write-Host '  已创建桌面快捷方式' -ForegroundColor Green
            }
        }
    }
}

# 3) 开机启动：HKCU Run，值为带引号的完整路径 + --tray
if (-not $NoAutoStart) {
    $command = '"' + $exePath + '" --tray'
    if ($PSCmdlet.ShouldProcess(($runKeyPath + ' :: ' + $runValueName), ('写入 ' + $command))) {
        if (-not (Test-Path -LiteralPath $runKeyPath)) {
            New-Item -Path $runKeyPath -Force | Out-Null
        }
        New-ItemProperty -Path $runKeyPath -Name $runValueName -Value $command -PropertyType String -Force | Out-Null
        Write-Host '  已设置开机启动（HKCU，可在程序设置里关闭）' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host '安装完成。' -ForegroundColor Cyan
Write-Host ('可执行文件: ' + $exePath)
Write-Host '启动: 开始菜单 → PeripheralBattery，或双击安装目录里的 exe'
Write-Host '卸载: powershell -ExecutionPolicy Bypass -File installer\uninstall.ps1'
Write-Host ('配置与日志保留在: ' + (Join-Path $env:APPDATA 'PeripheralBattery'))
