param(
    [string]$Tag = 'v1.8.4',
    [string]$Zip,
    [string]$Notes = 'docs\releases\v1.8.4.md'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$cn = [string]([char]0x5916) + [char]0x8BBE + [char]0x7535 + [char]0x91CF + [char]0x76D1 + [char]0x63A7
if (-not $Zip) { $Zip = Join-Path $root ("dist\2.4G" + $cn + "-" + $Tag + ".zip") }

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
