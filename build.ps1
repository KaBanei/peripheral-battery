#requires -Version 5.1
<#
  Build script for the 2.4G peripheral battery monitor.
  ASCII-only on purpose: Windows PowerShell 5.1 decodes BOM-less .ps1 as ANSI.

  Usage:
    powershell -ExecutionPolicy Bypass -File build.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1 -Probe
    powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir _build\ui
    powershell -ExecutionPolicy Bypass -File build.ps1 -Run
#>
param(
    [switch]$Probe,
    [switch]$Run,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'csc.exe not found: .NET Framework 4.x is required.' }

if ($OutDir) {
    if ([System.IO.Path]::IsPathRooted($OutDir)) { $dist = $OutDir }
    else { $dist = Join-Path $root $OutDir }
}
else { $dist = Join-Path $root 'dist' }

$bin = Join-Path $dist 'app'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll'
)

function Add-CommonArgs {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Target,
        [string]$OutPath,
        [string]$MainClass,
        [string]$IconPath
    )
    $List.Add('/nologo')
    $List.Add('/target:' + $Target)
    $List.Add('/platform:x64')
    $List.Add('/optimize+')
    $List.Add('/codepage:65001')
    $List.Add('/out:' + $OutPath)
    if ($MainClass) { $List.Add('/main:' + $MainClass) }
    $manifest = Join-Path $root 'src\app.manifest'
    if (Test-Path $manifest) { $List.Add('/win32manifest:' + $manifest) }
    if ($IconPath -and (Test-Path $IconPath)) { $List.Add('/win32icon:' + $IconPath) }
    foreach ($r in $refs) { $List.Add($r) }
}

function Invoke-Csc {
    param([System.Collections.Generic.List[string]]$ArgList)
    $out = & $csc $ArgList.ToArray() 2>&1
    $code = $LASTEXITCODE
    $out | ForEach-Object {
        if ($_ -match 'error') { Write-Host $_ -ForegroundColor Red }
        elseif ($_ -match 'warning') { Write-Host $_ -ForegroundColor DarkYellow }
    }
    if ($code -ne 0) { throw ('compilation failed (exit ' + $code + ')') }
}

Write-Host '== building PeripheralBattery.exe =='
$appSources = @(Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs |
                ForEach-Object { $_.FullName })
$exe = Join-Path $bin 'PeripheralBattery.exe'
$argsApp = New-Object 'System.Collections.Generic.List[string]'
Add-CommonArgs -List $argsApp -Target 'winexe' -OutPath $exe -MainClass 'Pb.App.Program' -IconPath (Join-Path $root 'assets\app.ico')
foreach ($s in $appSources) { $argsApp.Add($s) }
Invoke-Csc -ArgList $argsApp
Write-Host ('  OK ' + $exe) -ForegroundColor Green

if ($Probe) {
    Write-Host '== building BatteryProbe.exe =='
    $probeSources = @()
    $probeSources += Get-ChildItem -Path (Join-Path $root 'src\Core') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
    $probeSources += Get-ChildItem -Path (Join-Path $root 'tools\Probe') -Recurse -Filter *.cs -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    $probeExe = Join-Path $bin 'BatteryProbe.exe'
    $argsProbe = New-Object 'System.Collections.Generic.List[string]'
    Add-CommonArgs -List $argsProbe -Target 'exe' -OutPath $probeExe -MainClass 'Pb.Tools.ProbeProgram' -IconPath $null
    foreach ($s in $probeSources) { $argsProbe.Add($s) }
    Invoke-Csc -ArgList $argsProbe
    Write-Host ('  OK ' + $probeExe) -ForegroundColor Green

    Write-Host '== building VgnProbe.exe =='
    $vgnSources = @()
    $vgnSources += Get-ChildItem -Path (Join-Path $root 'src\Core') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
    $vgnSources += Get-ChildItem -Path (Join-Path $root 'tools\VgnProbe') -Recurse -Filter *.cs -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    $vgnExe = Join-Path $bin 'VgnProbe.exe'
    $argsVgn = New-Object 'System.Collections.Generic.List[string]'
    Add-CommonArgs -List $argsVgn -Target 'exe' -OutPath $vgnExe -MainClass 'VgnProbeProgram' -IconPath $null
    foreach ($s in $vgnSources) { $argsVgn.Add($s) }
    Invoke-Csc -ArgList $argsVgn
    Write-Host ('  OK ' + $vgnExe) -ForegroundColor Green
}

if ($Run) { Start-Process -FilePath $exe }
Write-Host 'build finished.' -ForegroundColor Cyan