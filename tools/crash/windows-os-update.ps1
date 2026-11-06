# 只安装 Windows 11 自身的版本升级 / 累积更新，然后把更新暂停写回去。
# 不安装 .NET、Visual Studio、PowerShell、恶意软件删除工具、VC++、任何驱动。
#
#   .\windows-os-update.ps1            # 只列出会装 / 会跳过的项，不改系统
#   .\windows-os-update.ps1 -Install   # 下载并安装命中的系统更新；结束时暂停到 -PauseUntil
#
# 双击请用同目录 windows-os-update.bat（自提权）。
# 本机故意把 Windows 更新暂停到 2045，-Install 结束时会把这个暂停写回去。

[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Yes,
    [string]$PauseUntil = '2045-12-08T07:13:02Z'
)

$ErrorActionPreference = 'Stop'
$LogPath = Join-Path $PSScriptRoot 'windows-os-update.log'

function Write-Log([string]$Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $Message
    Write-Host $line
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

function Test-OsUpdate($Update) {
    if ($Update.Type -eq 2) { return $false }
    $title = [string]$Update.Title
    $cats = New-Object System.Collections.Generic.List[string]
    foreach ($c in @($Update.Categories)) { [void]$cats.Add([string]$c.Name) }
    $catText = $cats -join '|'
    if ($catText -match 'Driver') { return $false }
    if ($title -match '\.NET|Visual Studio|PowerShell|Redistributable|Malicious Software Removal|恶意软件|Framework') {
        return $false
    }
    if ($title -match '^Windows 11, version \d+H\d+') { return $true }
    if ($catText -match 'Upgrades' -and $title -match 'Windows 11') { return $true }
    if ($title -match 'Cumulative Update' -and $title -match 'Windows' -and $title -notmatch '\.NET') { return $true }
    if ($title -match '累积更新' -and $title -match 'Windows' -and $title -notmatch '\.NET|Framework') { return $true }
    return $false
}

function Get-UpdatePauseKey {
    return 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
}

function Clear-UpdatePause {
    $ux = Get-UpdatePauseKey
    if (-not (Test-Path -LiteralPath $ux)) { return }
    foreach ($name in @(
            'PauseFeatureUpdatesStartTime', 'PauseFeatureUpdatesEndTime',
            'PauseQualityUpdatesStartTime', 'PauseQualityUpdatesEndTime',
            'PauseUpdatesStartTime', 'PauseUpdatesExpiryTime')) {
        Remove-ItemProperty -LiteralPath $ux -Name $name -ErrorAction SilentlyContinue
    }
}

function Set-UpdatePause([string]$UntilUtc) {
    $ux = Get-UpdatePauseKey
    if (-not (Test-Path -LiteralPath $ux)) {
        New-Item -Path $ux -Force | Out-Null
    }
    $start = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $values = @{
        PauseFeatureUpdatesStartTime = $start
        PauseFeatureUpdatesEndTime   = $UntilUtc
        PauseQualityUpdatesStartTime = $start
        PauseQualityUpdatesEndTime   = $UntilUtc
        PauseUpdatesExpiryTime       = $UntilUtc
    }
    foreach ($name in $values.Keys) {
        New-ItemProperty -LiteralPath $ux -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null
    }
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$script:ClearedPause = $false

function Invoke-OsUpdate {
    $build = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    Write-Log ("当前系统 {0}.{1} {2}" -f $build.CurrentBuild, $build.UBR, $build.DisplayVersion)

    try {
        if ($Install) {
            if (-not (Test-Admin)) {
                Write-Log '安装需要管理员。请双击 windows-os-update.bat。'
                return 1
            }
            Write-Log '临时取消更新暂停，以便搜索。'
            Clear-UpdatePause
            $script:ClearedPause = $true
        }

        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        Write-Log '正在搜索待安装更新...'
        $result = $searcher.Search("IsInstalled=0 and IsHidden=0")

        $selected = New-Object -ComObject Microsoft.Update.UpdateColl
        $skipCount = 0
        foreach ($update in @($result.Updates)) {
            $kb = (@($update.KBArticleIDs) -join ',')
            if (Test-OsUpdate $update) {
                [void]$selected.Add($update)
                Write-Log ("[install] {0}  KB={1}" -f $update.Title, $kb)
            }
            else {
                $skipCount++
                Write-Log ("[skip]    {0}  KB={1}" -f $update.Title, $kb)
            }
        }

        if ($selected.Count -eq 0) {
            Write-Log '没有匹配的 Windows 11 版本升级或累积更新。'
            return 2
        }

        if (-not $Install) {
            Write-Log ("只列出，未安装。命中 {0} 项，跳过 {1} 项。要安装请加 -Install，或双击 windows-os-update.bat。" -f $selected.Count, $skipCount)
            return 0
        }

        Write-Log '只安装上面 [install] 的项。驱动和开发运行时不会装。结束后把更新暂停写回指定日期。'
        if (-not $Yes) {
            $answer = Read-Host '输入 Y 继续，其它键退出'
            if ($answer -ne 'Y' -and $answer -ne 'y') {
                Write-Log '已取消，不安装。'
                return 0
            }
        }

        $pendingDownload = New-Object -ComObject Microsoft.Update.UpdateColl
        for ($i = 0; $i -lt $selected.Count; $i++) {
            $update = $selected.Item($i)
            if (-not $update.EulaAccepted) { $update.AcceptEula() | Out-Null }
            if (-not $update.IsDownloaded) { [void]$pendingDownload.Add($update) }
        }

        if ($pendingDownload.Count -gt 0) {
            Write-Log ("开始下载 {0} 项。版本升级可能要几 GB，请等到本窗口自己结束。" -f $pendingDownload.Count)
            $downloader = $session.CreateUpdateDownloader()
            $downloader.Updates = $pendingDownload
            $downloaded = $downloader.Download()
            Write-Log ("下载结果 ResultCode={0} HResult=0x{1:X8}" -f $downloaded.ResultCode, ($downloaded.HResult -band 0xffffffff))
            if ($downloaded.ResultCode -ne 2 -and $downloaded.ResultCode -ne 3) {
                Write-Log '下载未成功，停止，不安装。'
                return 1
            }
        }
        else {
            Write-Log '命中的更新已经下载过，直接安装。'
        }

        # 安装过程可能自己重启。先写回暂停，避免重启后更新通道一直开着。
        Set-UpdatePause $PauseUntil
        $script:ClearedPause = $false
        Write-Log ("已把 Windows 更新暂停写回 {0}。" -f $PauseUntil)

        Write-Log '开始安装。可能会弹出 Windows 安装界面，让它自己走完。'
        $installer = $session.CreateUpdateInstaller()
        $installer.Updates = $selected
        $installed = $installer.Install()
        Write-Log ("安装结果 ResultCode={0} RebootRequired={1} HResult=0x{2:X8}" -f $installed.ResultCode, $installed.RebootRequired, ($installed.HResult -band 0xffffffff))
        if ($installed.ResultCode -eq 2 -or $installed.ResultCode -eq 3) {
            Write-Log '安装步骤已结束。请重启一次。不要在同一次重启里刷 BIOS。'
            return 0
        }
        Write-Log '安装未成功。设置页里不要改点「全部下载并安装」，那里包含驱动。'
        return 1
    }
    finally {
        if ($script:ClearedPause) {
            Set-UpdatePause $PauseUntil
            Write-Log ("已把 Windows 更新暂停写回 {0}。" -f $PauseUntil)
            $script:ClearedPause = $false
        }
    }
}

exit (Invoke-OsUpdate)
