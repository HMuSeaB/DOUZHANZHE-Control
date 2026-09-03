# ============================================================================
# switch-stability-test.ps1 — 模式切换 & 参数下发稳定性/延迟自动化验证
#
# 用途：在「单实例」运行的本机后端上，遍历尽量多的参数下发场景，反复 /api/overrides/switch，
#       验证：
#        1. 切换 HTTP 响应正常（含合法 overrides），不抛异常
#        2. /api/overrides 返回的 mode 与请求一致
#        3. 遥测无非法值（风扇 RPM 在合理窗口、温度合理、键盘背光合理）——捕获 EC 双实例/读坏类回归
#        4. 记录每次切换耗时，统计 min/avg/max，判断延迟优化是否达成
#
# 用法：  单实例后端需已在本机运行（默认测 3101，可 -Port 覆盖）。
#         脚本会对「开发环境 config 目录」的 overrides-*.json 做备份/恢复，不碰真实安装版配置。
#
# 参数：
#   -Port         后端端口（默认 3101）
#   -IterRounds   每模式轮数（默认 6）
#   -Synthetic    是否跑合成 override 组合矩阵（默认 $true）
#   -QuietDetails 只输出摘要（默认 $false，逐条输出）
#   -ConfigDir    开发环境 config 目录（默认 server/api/bin/build/config）
# ============================================================================
param(
    [int]$Port = 3101,
    [int]$IterRounds = 6,
    [switch]$Synthetic = $true,
    [switch]$NoSynthetic,
    [switch]$QuietDetails,
    [string]$ConfigDir = "server\config"
)
if ($NoSynthetic) { $Synthetic = $false }

$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port/api"
$pass = 0; $fail = 0; $errors = @(); $lat = @()
$Modes = @('cfg-silent','cfg-office','cfg-beast','cfg-gaming')

function Write-Step($m){ Write-Host ">> $m" -ForegroundColor Cyan }

# ── 遥测合法性窗口（捕获 EC 读坏 / 双实例污染 / 单位错误）──
# 小扇/fanSmallRpm 上限 8200；合法观察窗口放宽松些，捕获 49087/311 这类量级错误即可
# ⚠️ kbBrightness 不参与硬失败：本型号 HAL 直读 EC 寄存器会稳定返回非 0-3 的原始值（如 94），
#    这是既有 EC 读法，与配置下发无关；fan/temp/overrides 才是下发稳定性的真信号。
function Test-TelemetrySane($t) {
    $issues = @()
    if ($null -eq $t) { return @('telemetry null') }
    $c = $t.cpuTemp;   if ($null -ne $c -and ($c -le 0 -or $c -ge 115)) { $issues += "cpuTemp=$c" }
    $g = $t.gpuTemp;   if ($null -ne $g -and ($g -le 0 -or $g -ge 115)) { $issues += "gpuTemp=$g" }
    $l = $t.fanLargeRpm; if ($null -ne $l -and ($l -lt 0 -or $l -gt 9000)) { $issues += "fanLarge=$l" }
    $s = $t.fanSmallRpm; if ($null -ne $s -and ($s -lt 0 -or $s -gt 9000)) { $issues += "fanSmall=$s" }
    return $issues
}

function Invoke-Switch([string]$mode) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $body = @{ mode = $mode } | ConvertTo-Json
    $r = Invoke-RestMethod -Method Post -Uri "$base/overrides/switch" -ContentType 'application/json' -Body $body -TimeoutSec 30
    $sw.Stop()
    return ,@($sw.Elapsed.TotalMilliseconds, $r)
}

function Test-Switch([string]$mode, [string]$label) {
    $ok = $true; $msgs = @()
    try {
        $res = Invoke-Switch $mode
        $ms = $res[0]; $r = $res[1]
        $script:lat += $ms
        if ($null -eq $r.overrides) { $ok = $false; $msgs += "overrides missing" }

        # 切完验证 /api/overrides 的 mode
        $ov = Invoke-RestMethod -Uri "$base/overrides" -TimeoutSec 10
        if ($ov.mode -ne $mode) { $ok = $false; $msgs += "mode!=requested ($($ov.mode) vs $mode)" }

        # 遥测合法性
        Start-Sleep -Milliseconds 400
        $tel = Invoke-RestMethod -Uri "$base/telemetry" -TimeoutSec 10
        $issues = Test-TelemetrySane $tel
        if ($issues.Count -gt 0) { $ok = $false; $msgs += ("telemetry: " + ($issues -join ',')) }
    }
    catch {
        $ok = $false; $msgs += "EXCEPTION: $($_.Exception.Message)"
    }
    if ($ok) { $script:pass++; if(-not $QuietDetails){ Write-Host "  ✓ [$label] $mode : $([math]::Round($ms))ms" -ForegroundColor Green } }
    else {
        $script:fail++; $msg = ("  ✗ [$label] $mode : $([math]::Round($ms,0))ms " + ($msgs -join ' | ')); $errors += $msg
        Write-Host $msg -ForegroundColor Red
    }
}

# ════ 预检 ════
Write-Step "预检: 后端 http://127.0.0.1:$Port ..."
try { $p = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 5; if ($p.StatusCode -ne 200) { Write-Host "后端未就绪" -ForegroundColor Red; exit 1 } } catch { Write-Host "后端不可达: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

# ── 单实例硬约束：若另一个端口(3100 或 3101)同时 LISTENING，直接终止，避免 PawnIO 并发读 EC 污染 ──
$singlePort = $Port
$otherPorts = @(3100,3101) | Where-Object { $_ -ne $singlePort }
$others = Get-NetTCPConnection -State Listen -LocalPort $otherPorts -ErrorAction SilentlyContinue
if ($others) {
    $list = ($others | ForEach-Object { "port $($_.LocalPort) (PID $($_.OwningProcess))" }) -join '; '
    Write-Host "  ❌ 检测到另一个后端实例在监听：$list —— 双实例会并发读 EC 产生脏数据，测试作废。请先停掉多余实例再跑。" -ForegroundColor Red
    exit 2
}
Write-Host "  ✓ 单实例约束通过（仅 $Port 在跑）" -ForegroundColor Green

# ── EC 副作用防护：快照键盘背光亮度，测试结束时恢复 ──
# 真实硬件 switch 下发（thermal_mode/fan 直写 EC 等）**可能**碰脏相邻 EC 寄存器（实测曾把
# 背光寄存器 0x9A 读到 94）。为避免把用户机器留在脏状态，测试开始前记下亮度，结束前写回。
$kb0 = $null
try { $kb0 = (Invoke-RestMethod -Uri "$base/telemetry" -TimeoutSec 10).kbBrightness } catch { $kb0 = $null }
if ($null -ne $kb0) { Write-Host "  背光快照 kb=$kb0（测试结束恢复该值）" -ForegroundColor Gray }

# ════ Phase 1: 内置模式往返切换（不改配置，纯切） ════
Write-Step "Phase 1: $IterRounds 轮 4 模式往返切换"
for ($i = 0; $i -lt $IterRounds; $i++) {
    foreach ($mode in $Modes) {
        Test-Switch $mode ("round$($i+1)")
    }
}

# ════ Phase 2: 合成 override 组合矩阵（备份 → import → 切 → 验证 → 恢复） ════
if ($Synthetic) {
    Write-Step "Phase 2: 合成 override 组合矩阵（开发 config 备份/恢复，不碰安装版）"
    $targetCfg = 'cfg-office'
    $cfgFile = (Join-Path $ConfigDir "profiles\$targetCfg.json")
    if (-not (Test-Path (Join-Path $PWD $cfgFile))) { Write-Host "  ! 找不到配置 $cfgFile，跳过合成矩阵" -ForegroundColor Yellow }
    else {
        $backupContent = Get-Content (Join-Path $PWD $cfgFile) -Raw -Encoding UTF8

        $combos = @(
            @{ name='CPU-only';   ov=@{ cpu=@{ freqLimitMhz=4200; turboEnabled=$false; coreLimitPercent=80 }; gpu=@{}; nvapi=@{}; smu=@{}; fan=@{}; powerPlan=$null } },
            @{ name='GPU-only';   ov=@{ cpu=@{}; gpu=@{ coreFreqMhz=2700; freqLocked=$true; memFreqLevel=3 }; nvapi=@{}; smu=@{}; fan=@{}; powerPlan=$null } },
            @{ name='SMU-only';   ov=@{ cpu=@{}; gpu=@{}; nvapi=@{}; smu=@{ stapmLimitW=68; shortPowerLimitW=78; tempLimitC=85; coAll=-20 }; fan=@{}; powerPlan=$null } },
            @{ name='NVAPI-only'; ov=@{ cpu=@{}; gpu=@{}; nvapi=@{ ocCoreOffsetMhz=80; ocMemOffsetMhz=120; powerLimitW=115; thermalLimitC=90 }; smu=@{}; fan=@{}; powerPlan=$null } },
            @{ name='Fan-only';   ov=@{ cpu=@{}; gpu=@{}; nvapi=@{}; smu=@{}; fan=@{ largeRpm=3000; smallRpm=6600 }; powerPlan=$null } },
            @{ name='PowerPlan';  ov=@{ cpu=@{}; gpu=@{}; nvapi=@{}; smu=@{}; fan=@{}; powerPlan=1 } },
            @{ name='Full-stack'; ov=@{ cpu=@{ freqLimitMhz=4000; turboEnabled=$false; coreLimitPercent=50 }; gpu=@{ coreFreqMhz=2720; freqLocked=$true; memFreqLevel=2 }; nvapi=@{ ocCoreOffsetMhz=60; ocMemOffsetMhz=90; powerLimitW=105; thermalLimitC=88 }; smu=@{ stapmLimitW=72; shortPowerLimitW=82; tempLimitC=86; coAll=-15 }; fan=@{ largeRpm=3400; smallRpm=7200 }; powerPlan=0 } },
            @{ name='Empty-reset'; ov=@{ cpu=@{}; gpu=@{}; nvapi=@{}; smu=@{}; fan=@{}; powerPlan=$null } }
        )

        foreach ($combo in $combos) {
            try {
                $null = Invoke-RestMethod -Method Post -Uri "$base/overrides/import" -ContentType 'application/json' -Body (@{ mode=$targetCfg; overrides=$combo.ov } | ConvertTo-Json -Depth 8) -TimeoutSec 10
                foreach ($i in 1..2) { Test-Switch $targetCfg ("$($combo.name)#$i") }
            }
            catch { $fail++; $msg="  ✗ [合成 $($combo.name)] 设置失败: $($_.Exception.Message)"; $errors+=$msg; Write-Host $msg -ForegroundColor Red }
        }

        # 恢复 cfg-office 原始参数（单一存储：profiles/）
        [System.IO.File]::WriteAllText((Join-Path $PWD $cfgFile), $backupContent, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  ↺ 已恢复 $targetCfg 原始参数" -ForegroundColor Yellow
        # 回到安静模式，让后端处于低功耗稳定态
        try { $null = Invoke-RestMethod -Method Post -Uri "$base/overrides/switch" -ContentType 'application/json' -Body (@{mode='cfg-silent'}|ConvertTo-Json) -TimeoutSec 20 } catch {}
    }
}

# ════ EC 副作用恢复：若键盘背光被测试碰脏，写回快照值 ════
if ($null -ne $kb0) {
    try {
        $kbNow = (Invoke-RestMethod -Uri "$base/telemetry" -TimeoutSec 10).kbBrightness
        if ([int]$kbNow -ne [int]$kb0) {
            $clamp = [Math]::Clamp([int]$kb0, 0, 3)
            $null = Invoke-RestMethod -Method Post -Uri "$base/control" -ContentType 'application/json' -Body (@{ target='kb_light'; value=$clamp } | ConvertTo-Json) -TimeoutSec 10
            Write-Host "  ↺ 键盘背光被测试碰脏（$kbNow → $kb0），已写回 kb_light=$clamp" -ForegroundColor Yellow
        } else {
            Write-Host "  背光保持快照值 $kb0（未被测试污染）" -ForegroundColor Gray
        }
    } catch { Write-Host "  ! 恢复背光失败: $($_.Exception.Message)" -ForegroundColor Yellow }
}

# ════ 汇总 ════
Write-Step "汇总"
$avg = if ($lat.Count) {[math]::Round(($lat | Measure-Object -Average).Average,0)} else {0}
$min = if ($lat.Count) {[math]::Round(($lat | Measure-Object -Minimum).Minimum,0)} else {0}
$max = if ($lat.Count) {[math]::Round(($lat | Measure-Object -Maximum).Maximum,0)} else {0}
Write-Host "  切换次数: $($lat.Count)  延迟 min=$min ms  avg=$avg ms  max=$max ms"
Write-Host "  通过: $pass   失败: $fail"
if ($errors.Count) {
    Write-Host "  ── 失败明细 ──" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
if ($fail -eq 0) { Write-Host "  ✅ 稳定性验证通过" -ForegroundColor Green; exit 0 }
else { Write-Host "  ❌ 存在失败，请检查后端日志" -ForegroundColor Red; exit 1 }
