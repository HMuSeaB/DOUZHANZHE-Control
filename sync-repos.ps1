# sync-repos.ps1 — 统一同步脚本
# 由 Douzhanzhe.API.csproj 的 AutoCommitAfterBuild 自动调用
# 也可手动运行： powershell -NoProfile -File sync-repos.ps1
# 前端部署后也会调用： deploy.ps1 末尾
#
# 作用：
#   1. 主仓库按类别 auto-commit（frontend / api / chore / docs）
#   2. 主仓库自动 push
#   3. docs/ → 私有仓库 docs 分支
#   4. canvas-workspace/ → 私有仓库 canvas 分支

param(
    [switch]$Quiet,
    [switch]$SkipPush
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$privateRemote = "https://github.com/KanzakiK/DOUZHANZHE-Control-private.git"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
$hasError = $false

function Write-Log($msg) {
    if (-not $Quiet) { Write-Host "[sync] $msg" -ForegroundColor Green }
}

function Write-Warn($msg) {
    if (-not $Quiet) { Write-Host "[sync] $msg" -ForegroundColor Yellow }
}

function Write-Err($msg) {
    if (-not $Quiet) { Write-Host "[sync] $msg" -ForegroundColor Red }
}

# 按文件路径归类到 scope
function Get-Scope($path) {
    $p = $path.ToLower().Replace('\', '/')
    if ($p -like 'src/*' -or $p -eq 'index.html' -or $p -eq 'vite.config.js') { return 'frontend' }
    if ($p -like 'server/*') { return 'api' }
    if ($p -eq 'agents.md' -or $p -like 'docs/*' -or $p -like 'readme*') { return 'docs' }
    return 'chore'
}

# 从文件路径提取简短名称，用于描述
function Get-ShortName($path) {
    $p = $path.Replace('\', '/')

    if ($p -eq 'src/index.css') { return 'styles' }
    if ($p -eq 'src/index.html') { return 'index' }
    if ($p -eq 'src/app.jsx') { return 'App' }
    if ($p -eq 'vite.config.js') { return 'vite-config' }

    if ($p -like 'src/pages/*') {
        return [System.IO.Path]::GetFileNameWithoutExtension(($p -split '/')[2])
    }
    if ($p -like 'src/components/panels/*') {
        return [System.IO.Path]::GetFileNameWithoutExtension(($p -split '/')[3])
    }
    if ($p -like 'src/components/*') {
        return [System.IO.Path]::GetFileNameWithoutExtension(($p -split '/')[2])
    }
    if ($p -like 'src/*/*') {
        return ($p -split '/')[1]
    }
    if ($p -like 'src/*') {
        return [System.IO.Path]::GetFileNameWithoutExtension(($p -split '/')[1])
    }

    if ($p -like 'server/api/*') { return 'API' }
    if ($p -like 'server/hal/*') { return 'HAL' }
    if ($p -like 'server/shell/*') { return 'Shell' }
    if ($p -like 'server/*') { return 'server' }

    if ($p -like '*.ps1' -or $p -like '*.bat') { return 'scripts' }
    if ($p -like '*.csproj') { return 'project' }
    if ($p -eq 'package.json') { return 'package' }
    if ($p -eq 'package-lock.json') { return 'package-lock' }
    if ($p -eq 'agents.md') { return 'AGENTS' }

    return [System.IO.Path]::GetFileNameWithoutExtension($path)
}

function Get-Description($files) {
    $names = $files | ForEach-Object { Get-ShortName $_ } | Select-Object -Unique | Sort-Object
    $limited = $names | Select-Object -First 8
    $suffix = if ($names.Count -gt $limited.Count) { "..." } else { "" }
    return ($limited -join ", ") + $suffix
}

# 主仓库同步
function Sync-MainRepo {
    Push-Location $root

    # 先清理暂存区，确保按类别分组提交
    git reset -q 2>$null

    $files = git status --porcelain | ForEach-Object {
        # porcelain 格式: "XY filename" 或 "XY orig -> new"
        $_.Substring(3)
    } | Where-Object { $_ -ne '' }

    if (-not $files) {
        Pop-Location
        return $false
    }

    $groups = $files | Group-Object { Get-Scope $_ } | Sort-Object Name
    $committedAny = $false

    foreach ($g in $groups) {
        $scope = $g.Name
        $scopeFiles = $g.Group

        foreach ($f in $scopeFiles) {
            git add -f -- "$f"
        }

        $desc = Get-Description $scopeFiles
        $msg = "$scope`: 更新 $desc ($timestamp)"
        git commit -m "$msg" 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            $committedAny = $true
            Write-Log "$msg"
        } else {
            Write-Warn "$scope`: 提交失败或被跳过"
        }
    }

    if ($committedAny -and -not $SkipPush) {
        $branch = git branch --show-current
        if ($branch) {
            git push origin $branch 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Log "已推送至 origin/$branch"
            } else {
                Write-Err "推送 origin/$branch 失败"
                $script:hasError = $true
            }
        }
    }

    Pop-Location
    return $committedAny
}

# 子目录同步到私有仓库
function Sync-PrivateDir($dirName, $branch) {
    $dir = Join-Path $root $dirName
    if (-not (Test-Path "$dir\.git")) {
        Write-Warn "$dirName/ 未初始化 git，跳过"
        return
    }

    Push-Location $dir
    $files = git status --porcelain | ForEach-Object { $_.Substring(3) } | Where-Object { $_ -ne '' }
    if (-not $files) {
        Pop-Location
        return
    }

    git add -A
    $desc = Get-Description $files
    git commit -m "sync: $desc ($timestamp)" 2>&1 | Out-Null

    if ($LASTEXITCODE -eq 0) {
        git push origin $branch 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Log "$dirName/ → 私有仓库 ($branch 分支)"
        } else {
            Write-Warn "$dirName/ 推送失败"
            $script:hasError = $true
        }
    }
    Pop-Location
}

# 入口
Write-Log "开始同步..."
$mainCommitted = Sync-MainRepo
Sync-PrivateDir 'docs' 'docs'
Sync-PrivateDir 'canvas-workspace' 'canvas'

if (-not $mainCommitted) {
    Write-Log "主仓库无变更"
}

if ($hasError -and -not $Quiet) {
    Write-Err "部分同步失败，请检查网络或认证"
}
