# Long-running memory/handle soak test for Douzhanzhe.API.
# Samples process metrics to CSV; use with MemoryViewer for cross-check.
#
# Usage:
#   .\tools\soak-monitor.ps1                          # 4h @ 60s, default log dir
#   .\tools\soak-monitor.ps1 -DurationHours 2
#   .\tools\soak-monitor.ps1 -ProcessName Douzhanzhe.API -IntervalSeconds 30

param(
    [string]$ProcessName = "Douzhanzhe.API",
    [int]$IntervalSeconds = 60,
    [double]$DurationHours = 4,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "logs\soak"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$csv = Join-Path $OutputDir "soak-$ProcessName-$stamp.csv"
$end = (Get-Date).AddHours($DurationHours)

"timestamp,pid,handles,private_mb,working_set_mb,threads,cpu_s" | Out-File $csv -Encoding utf8

Write-Host "Soak monitor: $ProcessName every ${IntervalSeconds}s for ${DurationHours}h" -ForegroundColor Cyan
Write-Host "Log: $csv" -ForegroundColor Cyan
Write-Host "End: $end" -ForegroundColor DarkGray
Write-Host "Press Ctrl+C to stop early." -ForegroundColor DarkGray

$sample = 0
while ((Get-Date) -lt $end) {
    $procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $ProcessName not running — waiting..." -ForegroundColor Yellow
    } else {
        foreach ($p in $procs) {
            $privMb = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
            $wsMb = [math]::Round($p.WorkingSet64 / 1MB, 2)
            $cpuS = [math]::Round($p.CPU, 1)
            $line = "$(Get-Date -Format 'o'),$($p.Id),$($p.HandleCount),$privMb,$wsMb,$($p.Threads.Count),$cpuS"
            Add-Content $csv $line
            $sample++
            if ($sample -eq 1 -or ($sample % 10) -eq 0) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] PID=$($p.Id) Handles=$($p.HandleCount) Private=${privMb}MB WS=${wsMb}MB (#$sample)" -ForegroundColor Green
            }
        }
    }
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Host "Done. $sample samples -> $csv" -ForegroundColor Cyan
