# start-dev.ps1 - one-click dev environment launcher (dev API on 3101, installed app keeps 3100)
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$DevPort = 3101
# 注意：dotnet build 的输出在 bin\build；bin\run 是历史遗留目录，里面的二进制会长期停留在旧版本。
# 旧脚本指向 bin\run，导致 start-dev 起的是陈旧后端、代码改动完全不生效（2026-09-25 实测：
# bin\run 里是 8/29 的 Douzhanzhe.API.dll，bin\build 里才是当天的）。
$DevApiDir = Join-Path $Root 'server\api\bin\build'
$DevApiExe = Join-Path $DevApiDir 'Douzhanzhe.API.exe'
$DevApiDll = Join-Path $DevApiDir 'Douzhanzhe.API.dll'

function Write-Step($m) { Write-Host ">>> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "  OK: $m" -ForegroundColor Green }
function Write-Warn($m) { Write-Host "  ! $m" -ForegroundColor Yellow }

# ============================================================
# 1. ensure dev API on 3101 (installed app keeps 3100)
# ============================================================
Write-Step "1/3 ensure dev API on port $DevPort ..."
$listener = netstat -ano | Select-String "127.0.0.1:$DevPort" | Select-String 'LISTENING'
if ($listener) {
    $existingPid = [int](($listener -split '\s+')[-1])
    $exePath = $null
    try { $exePath = (Get-CimInstance Win32_Process -Filter "ProcessId = $existingPid").ExecutablePath } catch {}
    if ($exePath -and $exePath -like '*Douzhanzhe-Control*') {
        Write-Ok "Dev API already running (PID=$existingPid)"
    } else {
        Write-Warn "Port $DevPort occupied by unknown process (PID=$existingPid), skip auto start"
    }
} else {
    # 启动前先确认产物存在、且不比源码旧 —— 避免又静默起一个陈旧后端
    if (-not (Test-Path $DevApiExe) -and -not (Test-Path $DevApiDll)) {
        Write-Warn "未找到构建产物: $DevApiDir"
        Write-Warn "请先运行: dotnet build server\api\Douzhanzhe.API.csproj"
        exit 1
    }
    $newestSrc = Get-ChildItem -Path (Join-Path $Root 'server\api'), (Join-Path $Root 'server\hal') `
        -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $newestBin = Get-Item $DevApiDll -ErrorAction SilentlyContinue
    if ($newestSrc -and $newestBin -and $newestSrc.LastWriteTime -gt $newestBin.LastWriteTime) {
        Write-Warn "后端产物比源码旧：产物 $($newestBin.LastWriteTime)，最新源码 $($newestSrc.LastWriteTime) ($($newestSrc.Name))"
        Write-Warn "请先重新构建: dotnet build server\api\Douzhanzhe.API.csproj"
        exit 1
    }
    Write-Ok "port $DevPort is free, starting dev API ..."
    if (Test-Path $DevApiExe) {
        Start-Process -FilePath $DevApiExe -ArgumentList "--urls=http://127.0.0.1:$DevPort" -WorkingDirectory $DevApiDir -WindowStyle Hidden
    } else {
        Start-Process -FilePath 'dotnet' -ArgumentList @($DevApiDll, "--urls=http://127.0.0.1:$DevPort") -WorkingDirectory $DevApiDir -WindowStyle Hidden
    }
    Start-Sleep 2
}

# ============================================================
# 2. build frontend + deploy to wwwroot
# ============================================================
Write-Step '2/3 build frontend + deploy ...'
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
# 3. verify backend is serving
# ============================================================
Write-Step '3/3 verify ...'
Start-Sleep 1
$running = $false
for ($i = 0; $i -lt 8; $i++) {
    try {
        $resp = Invoke-WebRequest -Uri "http://127.0.0.1:$DevPort/" -UseBasicParsing -TimeoutSec 2
        if ($resp.StatusCode -eq 200) { $running = $true; break }
    } catch {}
    Start-Sleep 1
}
if ($running) { Write-Ok "Dev environment ready! http://127.0.0.1:$DevPort/" }
else { Write-Warn "API may not be ready yet, check: $DevApiDir" }
