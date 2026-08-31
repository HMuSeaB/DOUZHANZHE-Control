# Start test API on port 3101 without touching installed console (3100).
# Requires admin (app.manifest requireAdministrator).
# Usage (elevated PowerShell):
#   .\tools\start-test-api.ps1
#   .\tools\start-test-api.ps1 -Build

param(
    [switch]$Build,
    [int]$Port = 3101
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$TestDir = Join-Path $Root "dist\test-api"

function Ensure-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "Admin required, requesting UAC..." -ForegroundColor Yellow
        $scriptPath = if ($PSCommandPath) { $PSCommandPath } else { Join-Path $PSScriptRoot "start-test-api.ps1" }
        $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $scriptPath)
        if ($Build) { $args += "-Build" }
        $args += @("-Port", $Port)
        Start-Process powershell -Verb RunAs -ArgumentList $args
        exit 0
    }
}

Ensure-Admin

if ($Build) {
    Write-Host "[1/3] publish API..." -ForegroundColor Cyan
    Push-Location $Root
    dotnet publish server/api/Douzhanzhe.API.csproj -c Release -o dist/test-api --nologo
    npx vite build
    $pawn = Join-Path $Root "server\assets\PawnIO"
    if (Test-Path $pawn) {
        $dest = Join-Path $TestDir "assets\PawnIO"
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item (Join-Path $pawn "*") $dest -Force
    }
    if (Test-Path (Join-Path $TestDir "wwwroot")) { Remove-Item (Join-Path $TestDir "wwwroot") -Recurse -Force }
    Copy-Item (Join-Path $Root "dist") (Join-Path $TestDir "wwwroot") -Recurse -Force
    Pop-Location
}

$exe = Join-Path $TestDir "Douzhanzhe.API.exe"
if (-not (Test-Path $exe)) {
    Write-Host "Missing $exe — run: .\tools\start-test-api.ps1 -Build" -ForegroundColor Red
    exit 1
}

$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listener) {
    $occPid = $listener.OwningProcess
    Write-Host "Port $Port in use by PID $occPid. Stop it first if you need a restart." -ForegroundColor Yellow
    exit 1
}

Write-Host "[2/3] Starting test API on :$Port ..." -ForegroundColor Cyan
Start-Process -FilePath $exe -ArgumentList "--urls=http://127.0.0.1:$Port" -WorkingDirectory $TestDir -WindowStyle Hidden
Start-Sleep 3

Write-Host "[3/3] Health check..." -ForegroundColor Cyan
try {
    Invoke-RestMethod "http://127.0.0.1:$Port/api/health" -TimeoutSec 5 | Out-Null
    Write-Host "  OK  http://127.0.0.1:$Port/" -ForegroundColor Green
    Write-Host "  Installed app stays on http://127.0.0.1:3100/" -ForegroundColor DarkGray
} catch {
    Write-Host "  Health check failed: $_" -ForegroundColor Red
    exit 1
}

$procs = Get-Process Douzhanzhe.API -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending
if ($procs) {
    $p = $procs[0]
    $privMb = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
    Write-Host "  Newest API PID=$($p.Id) Handles=$($p.HandleCount) Private=${privMb}MB" -ForegroundColor Cyan
}
