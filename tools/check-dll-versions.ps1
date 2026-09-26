<#
.SYNOPSIS
  列出目录里所有 .NET 程序集的 AssemblyVersion；可选对比两个目录，揪出版本漂移。

.DESCRIPTION
  用途：抓「同一程序集在不同项目里引用了不同版本，合并发布时被对方覆盖」这类打包缺陷。

  典型场景（2026-09-26 实际踩到）：
    Douzhanzhe.API.csproj  -> TaskScheduler 2.12.2
    Douzhanzhe.Shell.csproj -> TaskScheduler 2.11.0
    打包时 Shell 的输出被合并进 dist/publish/api，Shell 的 2.11.0 覆盖了 API 需要的 2.12.2，
    安装包只带 2.11.0.0 → API 启动时按 2.12.2.0 请求该程序集 → FileNotFoundException
    → POST /api/auto-start 返回 500（TaskService 的类型解析失败发生在 JIT 期，逃出 try/catch）。

  ⚠️ 关键坑：**不能用文件大小判断程序集版本**。
     "2.11.0.0" 与 "2.12.2.0" 等长，改版本号**不改变文件大小**（实测两者都是 334848 字节）。
     必须读 AssemblyName。

  ⚠️ 误报提示：`System.*` 这类**共享框架自带**的程序集，版本漂移通常无害 ——
     框架依赖型应用运行时从 Microsoft.WindowsDesktop.App / Microsoft.NETCore.App 解析，
     本地副本会被忽略。真正要盯的是**第三方 NuGet 包**（如 TaskScheduler），它们必须精确匹配。
     （实测：EventLog 有 10.0.0.0 vs 8.0.0.0 的漂移但日志零报错；TaskScheduler 一漂移就 FileNotFoundException。）

.EXAMPLE
  .\check-dll-versions.ps1 -Path "D:\Tools\Douzhanzhe Console"
  .\check-dll-versions.ps1 -Path "dist\publish\api" -ComparePath "server\api\bin\build"
#>
param(
  [Parameter(Mandatory = $true)][string]$Path,
  [string]$ComparePath,
  [string]$Filter = "*.dll"
)

function Get-AssemblyVersions {
  param([string]$Dir)
  $map = @{}
  if (-not (Test-Path $Dir)) { return $map }
  Get-ChildItem -Path $Dir -Filter $Filter -File -ErrorAction SilentlyContinue | ForEach-Object {
    try {
      $an = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
      $map[$_.Name] = [pscustomobject]@{
        Name    = $_.Name
        Version = $an.Version.ToString()
        Size    = $_.Length
      }
    } catch {
      # 原生 DLL（无托管元数据）会抛异常，跳过即可
    }
  }
  return $map
}

$a = Get-AssemblyVersions -Dir $Path
Write-Output ("== {0}  ({1} 个托管程序集) ==" -f (Resolve-Path $Path -ErrorAction SilentlyContinue), $a.Count)

if (-not $ComparePath) {
  $a.Values | Sort-Object Name | Format-Table Name, Version, Size -AutoSize
  exit 0
}

$b = Get-AssemblyVersions -Dir $ComparePath
Write-Output ("== {0}  ({1} 个托管程序集) ==" -f (Resolve-Path $ComparePath -ErrorAction SilentlyContinue), $b.Count)

$drift = @()
foreach ($name in $a.Keys) {
  if ($b.ContainsKey($name) -and $b[$name].Version -ne $a[$name].Version) {
    $drift += [pscustomobject]@{
      Name    = $name
      VersionA = $a[$name].Version
      VersionB = $b[$name].Version
    }
  }
}

Write-Output ""
if ($drift.Count -eq 0) {
  Write-Output "无版本漂移 ✓"
} else {
  Write-Output ("发现 {0} 处版本不一致 ✗ —— 合并/发布时高版本可能被低版本覆盖：" -f $drift.Count)
  $drift | Format-Table Name, VersionA, VersionB -AutoSize
  exit 1
}
