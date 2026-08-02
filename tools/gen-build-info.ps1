# gen-build-info.ps1 - generate build-info.json (numeric version + short commit label)
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pkgPath = Join-Path $Root "package.json"
if (-not (Test-Path $pkgPath)) { throw "package.json not found: $pkgPath" }
$pkg = Get-Content $pkgPath -Raw -Encoding UTF8 | ConvertFrom-Json

$commit = ""
try {
    $commit = (& git -C $Root rev-parse --short=7 HEAD 2>$null).Trim()
} catch {
    $commit = ""
}
if (-not $commit) { $commit = "unknown" }

$version = [string]$pkg.version
$full = if ($commit -ne "unknown") { "$version-$commit" } else { $version }
$info = @{
    version = $version
    commit  = $commit
    full    = $full
    builtAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $Root "build-info.json"), $info, $utf8NoBom)
Write-Host "build-info: $full"
