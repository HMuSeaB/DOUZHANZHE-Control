# gen-build-info.ps1 - generate build-info.json (numeric version + short commit label)
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pkgPath = Join-Path $Root "package.json"
if (-not (Test-Path $pkgPath)) {
    Write-Host "gen-build-info: package.json not found: $pkgPath" -ForegroundColor Red
    exit 1
}
$pkg = Get-Content $pkgPath -Raw -Encoding UTF8 | ConvertFrom-Json

# Resolve git defensively: PATH first, then the usual install locations.
# A missing git is NOT a failure -- we fall back to a version-only label.
# (Previously "& git" threw when git was not on PATH, which left $LASTEXITCODE
#  non-zero and made callers believe this script had failed.)
function Resolve-GitExe {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }

    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)
    foreach ($root in $roots) {
        if (-not $root) { continue }
        foreach ($rel in @("Git\cmd\git.exe", "Programs\Git\cmd\git.exe")) {
            $p = [System.IO.Path]::Combine($root, $rel)
            if (Test-Path $p) { return $p }
        }
    }
    foreach ($p in @("D:\Git\cmd\git.exe", "C:\Git\cmd\git.exe")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

$commit = "unknown"
$gitExe = Resolve-GitExe
if (-not $gitExe) {
    Write-Host "gen-build-info: git not found, using version-only label" -ForegroundColor Yellow
} else {
    try {
        $out = & $gitExe -C $Root rev-parse --short=7 HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $out) {
            $candidate = ([string]$out).Trim()
            if ($candidate -match '^[0-9a-f]{4,40}$') { $commit = $candidate }
        }
    } catch {
        Write-Host "gen-build-info: git rev-parse failed ($($_.Exception.Message)), using version-only label" -ForegroundColor Yellow
    }
}

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

# Explicit success code: callers must not inherit a stale $LASTEXITCODE from
# whatever native command happened to run last inside this script.
exit 0
