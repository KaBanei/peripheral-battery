param(
    [string]$Path = (Join-Path $PSScriptRoot '..\dist')
)
# Removes Mark-of-the-Web (Zone.Identifier) from distributed files so SmartScreen
# stops treating them as "downloaded from the Internet".
$files = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue
$count = 0
foreach ($f in $files) {
    try { Unblock-File -LiteralPath $f.FullName -ErrorAction Stop; $count++ } catch { }
}
Write-Host ("Unblocked {0} file(s) under {1}" -f $count, (Resolve-Path $Path))
