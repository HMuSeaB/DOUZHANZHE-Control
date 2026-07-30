# deploy.ps1 - 一键构建前端并同步到 C# 后端所有 wwwroot 目录
# 用法: .\deploy.ps1          (构建 + 部署)
#       .\deploy.ps1 -SkipBuild  (仅部署，跳过构建)

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

# ── 1. 构建 ──
if (-not $SkipBuild) {
    Write-Host "[1/4] Vite build..." -ForegroundColor Cyan
    Push-Location $Root
    npx vite build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Pop-Location
} else {
    Write-Host "[1/4] Skip build (--SkipBuild)" -ForegroundColor Yellow
}

# ── 2. 定义目标目录 ──
$Dist = Join-Path $Root "dist"
$Targets = @(
    (Join-Path $Root "server\api\wwwroot"),
    (Join-Path $Root "server\api\bin\run\wwwroot"),
    (Join-Path $Root "server\api\bin\build\wwwroot")
)

# ── 3. 同步到每个 wwwroot ──
Write-Host "[2/4] Syncing to wwwroot directories..." -ForegroundColor Cyan

foreach ($Target in $Targets) {
    if (-not (Test-Path $Target)) {
        Write-Host "  SKIP (not found): $Target" -ForegroundColor DarkGray
        continue
    }

    $TargetAssets = Join-Path $Target "assets"

    # 清理旧的 JS/CSS (保留 favicon/icons/svg 等非构建文件)
    if (Test-Path $TargetAssets) {
        Get-ChildItem -Path $TargetAssets -File | Where-Object {
            $_.Name -match '\.(js|css)$'
        } | Remove-Item -Force
    }

    # 复制 dist -> wwwroot
    Copy-Item -Path (Join-Path $Dist "index.html") -Destination $Target -Force

    if (Test-Path $TargetAssets) {
        # assets 目录已存在，复制内容进去
        Copy-Item -Path (Join-Path $Dist "assets\*") -Destination $TargetAssets -Force
    } else {
        # assets 不存在，整体复制
        Copy-Item -Path (Join-Path $Dist "assets") -Destination $TargetAssets -Recurse -Force
    }

    Write-Host "  OK: $Target" -ForegroundColor Green
}

# ── 4. 结果与仓库同步 ──
$JsFile = (Get-ChildItem -Path (Join-Path $Dist "assets") -Filter "*.js" | Select-Object -First 1).Name
Write-Host "[3/4] Done! Deployed: $JsFile" -ForegroundColor Green
Write-Host ""
Write-Host "Remember to restart the C# backend if it's running." -ForegroundColor Yellow
Write-Host ""
Write-Host "[4/4] Syncing repositories..." -ForegroundColor Cyan
$SyncScript = Join-Path $Root "sync-repos.ps1"
if (Test-Path $SyncScript) {
    powershell -NoProfile -ExecutionPolicy Bypass -File $SyncScript
    Write-Host "[4/4] Repositories synced." -ForegroundColor Green
} else {
    Write-Warn "sync-repos.ps1 not found, skipping repository sync."
}
