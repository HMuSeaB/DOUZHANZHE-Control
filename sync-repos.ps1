# sync-repos.ps1 — 统一同步脚本
# 由 Douzhanzhe.API.csproj 的 AutoCommitAfterBuild 自动调用
# 也可手动运行：  powershell -NoProfile -File sync-repos.ps1
#
# 作用：
#   1. 主仓库 auto-commit（有变更时才提交）
#   2. docs/ → 私有仓库 docs 分支
#   3. canvas-workspace/ → 私有仓库 canvas 分支

param([switch]$Quiet)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$privateRemote = "https://github.com/KanzakiK/DOUZHANZHE-Control-private.git"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
$hasError = $false

function Write-Log($msg) {
    if (-not $Quiet) { Write-Host "[sync] $msg" -Foreground Green }
}

function Write-Warn($msg) {
    if (-not $Quiet) { Write-Host "[sync] $msg" -Foreground Yellow }
}

Write-Log "检查变更..."

# ── 1. 主仓库 ──
Push-Location $root
$hasChanges = git status --porcelain | Select-Object -First 1
if ($hasChanges) {
    git add -A
    $files = git diff --cached --name-only | Select-Object -First 10; $filesStr = if ($files) { " (" + ($files -join ", ") + ")" } else { "" }; git commit -m "auto: 编译成功 $timestamp$filesStr"
    Write-Log "主仓库已提交"
}
Pop-Location

# ── 2. docs/ → 私有仓库 docs 分支 ──
$docsDir = Join-Path $root "docs"
if (Test-Path "$docsDir\.git") {
    Push-Location $docsDir
    $hasChanges = git status --porcelain | Select-Object -First 1
    if ($hasChanges) {
        git add -A
        $files = git diff --cached --name-only | Select-Object -First 10; $filesStr = if ($files) { " (" + ($files -join ", ") + ")" } else { "" }; git commit -m "sync: $timestamp$filesStr"
        git push origin docs 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Log "docs → 私有仓库 (docs 分支)"
        } else {
            Write-Warn "docs 推送失败，稍后重试即可"
            $hasError = $true
        }
    }
    Pop-Location
} else {
    Write-Warn "docs/ 未初始化 git，跳过（运行 git init 后重试）"
}

# ── 3. canvas-workspace/ → 私有仓库 canvas 分支 ──
$canvasDir = Join-Path $root "canvas-workspace"
if (Test-Path "$canvasDir\.git") {
    Push-Location $canvasDir
    $hasChanges = git status --porcelain | Select-Object -First 1
    if ($hasChanges) {
        git add -A
        $files = git diff --cached --name-only | Select-Object -First 10; $filesStr = if ($files) { " (" + ($files -join ", ") + ")" } else { "" }; git commit -m "sync: $timestamp$filesStr"
        git push origin canvas 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Log "canvas-workspace → 私有仓库 (canvas 分支)"
        } else {
            Write-Warn "canvas-workspace 推送失败，稍后重试即可"
            $hasError = $true
        }
    }
    Pop-Location
} else {
    Write-Warn "canvas-workspace/ 未初始化 git，跳过"
}
if (-not $hasChanges -and -not $hasError) { Write-Log "全部仓库已同步，无变更" }
if ($hasError -and -not $Quiet) {
    Write-Host "[sync] 部分推送失败，检查网络或认证" -Foreground Red
}



