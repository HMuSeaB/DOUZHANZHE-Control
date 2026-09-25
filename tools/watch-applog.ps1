# Douzhanzhe 运行日志观察台
#
# 后端 (Douzhanzhe.API) 与 Shell 都写同一个文件：
#   %LOCALAPPDATA%\Douzhanzhe Console\logs\app.log
# 本脚本实时跟踪该文件，并把关键事件高亮出来，用于验证：
#   1. 风扇手动调速是否持久化      → [overrides] 行
#   2. 25249 RPM 脏读是否消失      → [FanRpm] 行
#   3. Shell「重启后端」死循环是否停止 → [Shell] 重启后端 行
#
# 用法：
#   .\tools\watch-applog.ps1                 # 打印最近 40 行后实时跟踪
#   .\tools\watch-applog.ps1 -Tail 200       # 先回看 200 行
#   .\tools\watch-applog.ps1 -Summary        # 只统计关键事件次数，不跟踪
#   .\tools\watch-applog.ps1 -LogPath D:\x\app.log
#
# 退出：Ctrl+C

param(
    [string]$LogPath = "",
    [int]$Tail = 40,
    [switch]$Summary,
    [switch]$NoColor
)

$ErrorActionPreference = "Stop"

if (-not $LogPath) {
    $LogPath = Join-Path $env:LOCALAPPDATA "Douzhanzhe Console\logs\app.log"
}

if (-not (Test-Path $LogPath)) {
    Write-Host "找不到日志文件：" -ForegroundColor Red
    Write-Host "  $LogPath" -ForegroundColor Red
    Write-Host "请先启动 Douzhanzhe.Shell.exe（后端由 Shell 拉起），或确认安装目录。" -ForegroundColor Yellow
    exit 1
}

# ---- 关键事件模式表：标签 → 匹配正则 → 颜色 ----
$Patterns = @(
    @{ Name = "overrides 保存成功"; Re = '\[overrides\]\s*✓';                       Color = "Green"  }
    @{ Name = "overrides 保存失败"; Re = '\[overrides\]\s*✗';                       Color = "Red"    }
    @{ Name = "overrides 未知 id";  Re = '未知配置 id';                              Color = "Red"    }
    @{ Name = "风扇脏读被丢弃";     Re = '读数超出物理上限已丢弃';                     Color = "Magenta"}
    @{ Name = "Shell 重启后端";     Re = '重启后端';                                 Color = "Red"    }
    @{ Name = "健康检查失败";       Re = '健康检查连续失败';                           Color = "Yellow" }
    @{ Name = "HAL 驱动不可用";     Re = '硬件驱动不可用';                             Color = "DarkYellow" }
    @{ Name = "同源守卫拒绝";       Re = '同源守卫|跨站请求被拒|缺少或错误的会话令牌';   Color = "Yellow" }
    @{ Name = "EC 相关";            Re = '\[EC|EC_IO|ReadEc';                        Color = "Cyan"   }
)

function Get-LineColor([string]$line) {
    foreach ($p in $Patterns) {
        if ($line -match $p.Re) { return $p.Color }
    }
    return $null
}

# ---- Summary 模式 ----
if ($Summary) {
    $all = Get-Content $LogPath
    $total = $all.Count
    $first = if ($total) { ($all[0] -split '\]')[0].TrimStart('[') } else { "-" }
    $last = if ($total) { ($all[-1] -split '\]')[0].TrimStart('[') } else { "-" }

    Write-Host ""
    Write-Host "日志：$LogPath" -ForegroundColor Cyan
    Write-Host "总行数：$total" -ForegroundColor Gray
    Write-Host "时间范围：$first  →  $last" -ForegroundColor Gray
    Write-Host ""
    Write-Host ("{0,-24} {1,6}" -f "关键事件", "次数") -ForegroundColor White
    Write-Host ("{0,-24} {1,6}" -f ("-" * 24), ("-" * 6)) -ForegroundColor DarkGray
    foreach ($p in $Patterns) {
        $n = ($all | Select-String -Pattern $p.Re).Count
        $c = if ($n -gt 0) { $p.Color } else { "DarkGray" }
        Write-Host ("{0,-24} {1,6}" -f $p.Name, $n) -ForegroundColor $c
    }
    Write-Host ""
    Write-Host "被同源守卫拒绝的端点 TOP：" -ForegroundColor White
    $rej = $all | Select-String -Pattern '拒绝\s+(GET|POST|PUT|DELETE)\s+(\S+)' -AllMatches |
        ForEach-Object { $_.Matches } | ForEach-Object { "$($_.Groups[1].Value) $($_.Groups[2].Value)" }
    if ($rej) {
        $rej | Group-Object | Sort-Object Count -Descending | Select-Object -First 8 | ForEach-Object {
            $c = if ($_.Name -match '/api/health') { "Red" } else { "DarkGray" }
            Write-Host ("  {0,6}  {1}" -f $_.Count, $_.Name) -ForegroundColor $c
        }
        Write-Host "  （/api/health 被拒 = Shell 看门狗探活失败 → 触发重启循环）" -ForegroundColor DarkGray
    } else {
        Write-Host "  （无）" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "解读：" -ForegroundColor White
    Write-Host "  · 「Shell 重启后端」= 0  → 403 死循环已消除（修复前是每 16s 一次）" -ForegroundColor Gray
    Write-Host "  · 「overrides 保存成功」> 0 且失败 = 0 → 调速已真正落盘" -ForegroundColor Gray
    Write-Host "  · 「风扇脏读被丢弃」偶尔出现属正常；关键是 UI 上不再显示 25249" -ForegroundColor Gray
    exit 0
}

# ---- 实时跟踪模式 ----
$size = (Get-Item $LogPath).Length
$shown = 0
$tailLines = Get-Content $LogPath -Tail $Tail
Write-Host ""
Write-Host "=== 观察：$LogPath ===" -ForegroundColor Cyan
Write-Host "=== 最近 $($tailLines.Count) 行（Ctrl+C 退出） ===" -ForegroundColor DarkGray
Write-Host ""
foreach ($line in $tailLines) {
    $c = Get-LineColor $line
    if ($c -and -not $NoColor) { Write-Host $line -ForegroundColor $c } else { Write-Host $line }
    $shown++
}

Write-Host ""
Write-Host "--- 以下为实时输出 ---" -ForegroundColor DarkCyan

while ($true) {
    Start-Sleep -Milliseconds 500
    $newSize = (Get-Item $LogPath).Length
    if ($newSize -lt $size) {
        # 日志被轮转/重建
        Write-Host "--- 日志已轮转，重新跟踪 ---" -ForegroundColor DarkYellow
        $size = 0
    }
    if ($newSize -gt $size) {
        $fs = [System.IO.File]::Open($LogPath, 'Open', 'Read', 'ReadWrite')
        try {
            $fs.Seek($size, 'Begin') | Out-Null
            $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
            $chunk = $sr.ReadToEnd()
            $sr.Close()
        } finally { $fs.Close() }
        $size = $newSize
        foreach ($line in ($chunk -split "`r?`n")) {
            if (-not $line.Trim()) { continue }
            $c = Get-LineColor $line
            if ($c -and -not $NoColor) { Write-Host $line -ForegroundColor $c } else { Write-Host $line }
        }
    }
}
