# start-dev.ps1 - one-click dev environment launcher
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$DevApiPath = Join-Path $Root 'server\api\bin\run\Douzhanzhe.API.exe'

function Write-Step($m) { Write-Host ">>> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host '  OK: $m' -ForegroundColor Green }
function Write-Warn($m) { Write-Host '  ! $m' -ForegroundColor Yellow }

# ============================================================
# 1. detect port 3100
# ============================================================
Write-Step '1/4 detect port 3100 ...'
$listener = netstat -ano | Select-String '127.0.0.1:3100' | Select-String 'LISTENING'
$needElevated = $false

if ($listener) {
    $existingPid = [int](($listener -split '\s+')[-1])
    try {
        $exePath = (Get-CimInstance Win32_Process -Filter "ProcessId = $existingPid").ExecutablePath
        if ($exePath -and $exePath -like '*Program Files*') {
            Write-Warn "Installed version running (PID=$existingPid)"
            $needElevated = $true
        } elseif ($exePath -and $exePath -like '*Douzhanzhe-Control*') {
            Write-Ok "Dev API already running (PID=$existingPid)"
        } else {
            Write-Warn "Unknown process on 3100 (PID=$existingPid)"
            $needElevated = $true
        }
    } catch {
        Write-Warn 'Cannot detect process path, try elevation'
        $needElevated = $true
    }
} else {
    Write-Ok 'port 3100 is free'
}

# ============================================================
# 2. elevate to handle API (if installed version is running)
# ============================================================
if ($needElevated) {
    Write-Step '2/4 elevate to handle API process ...'
    $elevatedFile = Join-Path $env:TEMP 'dz_dev_elevated.ps1'
    @'
$ErrorActionPreference = 'Stop'
Write-Host '[elevated] Stopping old API ...'
Get-Process -Name Douzhanzhe.API -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); Write-Host ('  killed PID=' + $_.Id) } catch { }
}
Start-Sleep 2
'@ | Out-File -FilePath $elevatedFile -Encoding UTF8
    Add-Content -Path $elevatedFile -Value "Start-Process -FilePath '$DevApiPath' -WorkingDirectory '$Root\server\api\bin\run' -WindowStyle Hidden"
    Write-Warn 'Elevating (may show UAC prompt) ...'
    Start-Process -FilePath powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$elevatedFile`"" -Verb RunAs -Wait
    Remove-Item $elevatedFile -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    Write-Ok 'API restarted'
}

# ============================================================
# 3. build frontend + deploy to wwwroot
# ============================================================
Write-Step '3/4 build frontend + deploy ...'
Push-Location $Root
try {
    if ($SkipBuild) { & '.\deploy.ps1' -SkipBuild }
    else { & '.\deploy.ps1' }
} catch {
    Write-Host "deploy.ps1 failed: $_" -ForegroundColor Red
    Pop-Location; exit 1
}
Pop-Location

# ============================================================
# 4. verify backend is serving
# ============================================================
Write-Step '4/4 verify ...'
Start-Sleep 1
$running = $false
for ($i = 0; $i -lt 8; $i++) {
    try {
        $resp = Invoke-WebRequest -Uri 'http://127.0.0.1:3100/' -UseBasicParsing -TimeoutSec 2
        if ($resp.StatusCode -eq 200) { $running = $true; break }
    } catch {}
    Start-Sleep 1
}
if ($running) { Write-Ok 'Dev environment ready! http://127.0.0.1:3100/' }
else { Write-Warn "API may not be ready yet, check: $DevApiPath" }