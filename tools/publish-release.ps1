param(
    [string]$Tag = 'v1.8.0',
    [string]$Zip = 'dist\2.4G外设电量监控-v1.8.0.zip',
    [string]$Notes = 'docs\releases\v1.8.0.md'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not (git tag --list $Tag)) {
    git tag -a $Tag -m $Tag
}

Write-Host "Pushing main..."
git push origin main
Write-Host "Pushing tag $Tag..."
git push origin $Tag

if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "Creating GitHub release via gh..."
    gh release create $Tag $Zip --title $Tag --notes-file $Notes
} else {
    Write-Host ""
    Write-Host "gh CLI not found. Finish the release in the browser:"
    Write-Host "  https://github.com/KaBanei/peripheral-battery/releases/new?tag=$Tag"
    Write-Host "Upload: $Zip"
    Write-Host "Notes file: $Notes"
}
