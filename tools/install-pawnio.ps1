# PawnIO 驱动开发环境安装脚本
# 用法: PowerShell (管理员) > .\tools\install-pawnio.ps1
#
# 从 server/tools/PawnIO_setup.exe 静默安装 PawnIO 内核驱动。
# PawnIO 是 namazso 开发的通用硬件访问驱动，替代 inpoutx64/WinRing0。

$ErrorActionPreference = "Stop"
$SetupPath = Join-Path $PSScriptRoot "..\server\tools\PawnIO_setup.exe"

# ---- 检测是否已安装 ----
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"
$installed = $null -ne (Get-ItemProperty -Path $regPath -Name "DisplayVersion" -ErrorAction SilentlyContinue)

if ($installed) {
    $ver = (Get-ItemProperty -Path $regPath -Name "DisplayVersion").DisplayVersion
    Write-Host "[PawnIO] 已安装 (v$ver)，跳过安装" -ForegroundColor Green
    exit 0
}

# ---- 管理员检查 ----
$isAdmin = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "[PawnIO] 请以管理员身份运行此脚本" -ForegroundColor Red
    exit 1
}

# ---- 检查安装包 ----
if (-not (Test-Path $SetupPath)) {
    Write-Host "[PawnIO] 未找到安装包: $SetupPath" -ForegroundColor Red
    Write-Host "[PawnIO] 请先运行: curl -L -o server/tools/PawnIO_setup.exe https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"
    exit 1
}

# ---- 静默安装 ----
Write-Host "[PawnIO] 正在安装..." -ForegroundColor Yellow
try {
    $proc = Start-Process -FilePath $SetupPath -ArgumentList "-install -silent" -Wait -PassThru -NoNewWindow
    if ($proc.ExitCode -ne 0) {
        Write-Host "[PawnIO] 安装失败 (exit code: $($proc.ExitCode))" -ForegroundColor Red
        exit $proc.ExitCode
    }
}
catch {
    Write-Host "[PawnIO] 安装异常: $_" -ForegroundColor Red
    exit 1
}

# ---- 验证安装 ----
$installed = $null -ne (Get-ItemProperty -Path $regPath -Name "DisplayVersion" -ErrorAction SilentlyContinue)
if ($installed) {
    $ver = (Get-ItemProperty -Path $regPath -Name "DisplayVersion").DisplayVersion
    Write-Host "[PawnIO] 安装成功 (v$ver)" -ForegroundColor Green
    Write-Host "[PawnIO] 设备节点: \\.\PawnIO 或 \\?\GLOBALROOT\Device\PawnIO"
    Write-Host "[PawnIO] 如设备未出现，请重启电脑" -ForegroundColor Yellow
}
else {
    Write-Host "[PawnIO] 安装可能未完成，请重启后再试" -ForegroundColor Red
    exit 1
}
