param(
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$exe = Join-Path $root 'dist\app\PeripheralBattery.exe'
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

if (-not $Version) {
    $Version = (Get-Item $exe).VersionInfo.FileVersion
    $parts = $Version.Split('.')
    if ($parts.Length -ge 3) { $Version = $parts[0] + '.' + $parts[1] + '.' + $parts[2] }
}

# Chinese product name assembled from code points so this script stays ASCII-only
# (Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI).
$cn = [string]([char]0x5916) + [char]0x8BBE + [char]0x7535 + [char]0x91CF + [char]0x76D1 + [char]0x63A7
$name = "2.4G" + $cn + "-v" + $Version
$rel = Join-Path $root ("dist\" + $name)
$zip = Join-Path $root ("dist\" + $name + ".zip")
Remove-Item $rel -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $rel | Out-Null
Copy-Item (Join-Path $root 'dist\app\PeripheralBattery.exe') $rel -Force
Copy-Item (Join-Path $root 'dist\app\BatteryProbe.exe') $rel -Force
Copy-Item (Join-Path $root 'dist\app\VgnProbe.exe') $rel -Force
$manual = Get-ChildItem (Join-Path $root 'dist\app') -Filter '*.txt' -File | Select-Object -First 1
if ($manual) { Copy-Item $manual.FullName $rel -Force }
Copy-Item (Join-Path $root 'README.md') $rel -Force
Copy-Item (Join-Path $root 'docs') (Join-Path $rel 'docs') -Recurse -Force
Copy-Item (Join-Path $root 'installer') (Join-Path $rel 'installer') -Recurse -Force
Copy-Item (Join-Path $root 'tools\unblock.ps1') $rel -Force
Copy-Item (Join-Path $root 'tools\enable-autostart.ps1') $rel -Force

$sums = Get-ChildItem $rel -Filter *.exe | ForEach-Object {
    (Get-FileHash $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name
}
Set-Content -Path (Join-Path $rel 'SHA256SUMS.txt') -Value $sums -Encoding ASCII
Compress-Archive -Path $rel -DestinationPath $zip -Force

Get-ChildItem (Join-Path $root 'dist') -Directory -Filter '2.4G*-v*' |
    Where-Object { $_.Name -ne $name } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem (Join-Path $root 'dist') -File -Filter '2.4G*-v*.zip' |
    Where-Object { $_.Name -ne ($name + '.zip') } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Release folder: $rel"
Write-Host "Zip: $zip"
Write-Host ("SHA256: " + (Get-FileHash $zip -Algorithm SHA256).Hash)
