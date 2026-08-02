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
    Write-Host "Generating build-info.json..." -ForegroundColor Cyan
    & (Join-Path $Root "tools\gen-build-info.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "build-info generation failed!" -ForegroundColor Red
        Pop-Location
        exit 1
    }
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
    (Join-Path $Root "server\api\bin\build\wwwroot"),
    (Join-Path $Root "server\shell\Douzhanzhe.Shell\bin\Debug\net8.0-windows\wwwroot"),
    (Join-Path $Root "server\shell\Douzhanzhe.Shell\bin\Release\net8.0-windows\wwwroot")
)
$TargetBases = @(
    (Join-Path $Root "server\api"),
    (Join-Path $Root "server\api\bin\run"),
    (Join-Path $Root "server\api\bin\build"),
    (Join-Path $Root "server\shell\Douzhanzhe.Shell\bin\Debug\net8.0-windows"),
    (Join-Path $Root "server\shell\Douzhanzhe.Shell\bin\Release\net8.0-windows")
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

# 写入固定版本文件，供后端 /api/update/check 读取
$Pkg = Get-Content (Join-Path $Root "package.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$AppVersion = [string]$Pkg.version
foreach ($Base in $TargetBases) {
    if (-not (Test-Path $Base)) { continue }
    $VersionFile = Join-Path $Base "version.txt"
    [System.IO.File]::WriteAllText($VersionFile, $AppVersion, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "  version.txt -> $Base" -ForegroundColor Green
    # build-info.json 只在运行时目录（bin/*）生成，避免把生成文件混入源码目录
    if ($Base -like "*\bin\*") {
        $BuildInfoFile = Join-Path $Root "build-info.json"
        if (Test-Path $BuildInfoFile) {
            Copy-Item $BuildInfoFile (Join-Path $Base "build-info.json") -Force
            Write-Host "  build-info.json -> $Base" -ForegroundColor Green
        }
    }
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
