# Douzhanzhe Console

> [!WARNING]
> **风险提示 & 兼容性声明 / Disclaimer**
>
> **兼容性**：本控制台仅在 **联想斗战者战7000锐龙版 2025款** 上测试通过。
> **Intel 酷睿版（战7000P 2026）** 也受支持但仅有 PL1/PL2 控制（暂无温度墙/降压等）。
> 其他机型使用可能导致部分功能不可用。请在非兼容机型上谨慎使用。
>
> **操作风险**：本工具提供的部分功能涉及超出厂商预设范围的硬件参数调整（包括但不限于 CPU 功耗墙、温度墙、GPU 超频、EC 寄存器直写等）。
> **使用此类功能可能导致硬件损坏、系统不稳定、数据丢失，或影响厂商保修及售后服务。**
>
> 请在充分了解相关风险后自行决定是否使用。**因使用本工具造成的一切硬件损坏、系统故障、保修失效等后果，需由用户自行承担，本工具及其开发者不承担任何责任。**
>
> **Compatibility**: Tested on **Lenovo Legion 7000 Ryzen 2025 Edition**. Intel Core variant also supported with PL1/PL2 control only.
>
> **Use at your own risk.** The developers assume no liability for any hardware damage, system failure, or warranty issues.

> [!TIP]
> **下载安装 / Download**
> 获取最新安装包请访问：[GitHub Releases](https://github.com/KanzakiK/DOUZHANZHE-Control/releases/latest)

---

**斗战者控制台** — 专为联想斗战者系列打造的开源硬件控制面板。
替代官方联想电脑管家，提供完整的硬件监控、性能调优和系统控制能力。

**Douzhanzhe Console** — Open-source hardware control panel for Lenovo Legion 7000 series.
A full-featured alternative to Lenovo Vantage.

---

## 功能 Features

**实时监控** — CPU/GPU 温度、频率、占用率、功耗、风扇转速、显存、内存与磁盘，
WebSocket 每秒推送全量遥测，含 5 条负载历史曲线。

**性能调优（AMD）** — CPU 功耗墙/温度墙/Curve Optimizer 降压 (PawnIO RyzenSMU.bin)、
GPU 超频偏移/锁频/温度限制 (NVAPI P/Invoke + nvidia-smi)，
四档模式预设一键切换（安静/均衡/野兽/斗战）。

**性能调优（Intel）** — PL1/PL2 功耗限制 (PawnIO IntelMSR.bin)，
CPU 频率限制/睿频/核心数限制 (powercfg)。

**全平台通用** — CPU 频率限制、睿频开关、核心数限制、电源计划管理（powercfg 跨平台通用）。

**自定义风扇曲线** — 独立标签页，SVG 可视化温度-转速曲线编辑器，
支持 12 点拖拽、保存/加载/启停/恢复，后台 FanCurveService 定时写入。

**GPU 模式** — 混合/集显/独显三档切换 (WMI MiInterface)，用户选择持久化到配置文件。

**系统控制** — 键盘背光亮度 (0-3)、FnLock/CapsLock/NumLock/触摸板锁定、
风扇目标转速直写、自定义背景图片、热键自定义。

**游戏自动切换** — 检测游戏进程自动切换性能模式，支持 Steam/Epic 游戏扫描和批量添加。

**个性化** — @dnd-kit 拖拽排序仪表盘、模块隐藏/显示、24 套主题皮肤。

**桌面集成** — WinForms + WebView2 原生桌面壳，系统托盘、开机自启（计划任务）。

## 安装 Installation

从 [Releases](https://github.com/KanzakiK/DOUZHANZHE-Control/releases) 页面下载最新安装包 `DouzhanzheConsole-*-Setup.exe`，双击运行即可。

安装程序会自动：安装 .NET 8 Runtime（如缺）、安装 PawnIO 内核驱动、清理旧版驱动残留。

> 安装包已签名，驱动安装需在 Windows 测试模式下或启用 TESTSIGNING。

## 快速开始（开发环境）

### 环境要求

| 工具 | 版本 | 验证命令 |
|------|------|----------|
| .NET SDK | 8.0 (net8.0-windows) | `dotnet --list-sdks` |
| Node.js | >= 18 | `node --version` |
| OS | Windows 10/11 x64 | — |
| 权限 | **管理员身份** | PawnIO + WMI 需要 |

### 启动（管理员 PowerShell）

```powershell
cd server/api
dotnet run --urls http://0.0.0.0:3100
```

访问 `http://127.0.0.1:3100`，Debug 面板在 `http://127.0.0.1:3100/debug`。

### 构建部署

```powershell
.\deploy.ps1          # 构建前端 + 同步到后端 wwwroot
```

## 技术栈

| 层级 | 技术 |
|------|------|
| 桌面壳 | WinForms + WebView2（单实例、托盘、开机自启） |
| 前端 | React 19 + Vite 8 + TailwindCSS 3 + @dnd-kit |
| 后端 | .NET 8 Minimal API + WMI + PawnIO |
| 内核驱动 | **PawnIO** v2.2.0.0（替代 inpoutx64 + WinRing0） |
| AMD SMU | PawnIO RyzenSMU.bin（替代 ryzenadj 子进程） |
| Intel MSR | PawnIO IntelMSR.bin |
| GPU 控制 | NVAPI P/Invoke（替代 KaronOC.dll）+ nvidia-smi |
| EC 直写 | PawnIO LpcACPIEC.bin |
| 安装包 | Inno Setup 6 |

## 项目结构

```
DOUZHANZHE-Control/
├── src/                        # React 前端
│   ├── App.jsx                 # 5 标签页 + 模式 Dock + 自定义背景
│   ├── components/
│   │   ├── SortableDashboard   # 拖拽仪表盘（11 张卡片内联渲染）
│   │   ├── panels/             # 7 个面板（含 IntelCpuPanel/IntelPowerPanel）
│   │   └── ui/                 # 10 个通用组件
│   ├── hooks/                  # useCardOrder, useControlState
│   └── services/               # uxtuAdapter (API 封装 + flattenBackendOverrides)
├── server/
│   ├── api/                    # .NET 8 后端
│   │   ├── Program.cs          # ~70 个端点 + WebSocket 遥测
│   │   ├── WmiInterface.cs     # WMI ACPI 硬件通信
│   │   └── TelemetryBackgroundService.cs
│   ├── hal/                    # 硬件抽象层
│   │   ├── DriverBridge.cs     # PawnIO EC IO（替代 inpoutx64）
│   │   ├── HardwareAbstractionLayer.cs
│   │   ├── AmdSmuController.cs # PawnIO RyzenSMU.bin（替代 ryzenadj）
│   │   └── IntelPowerController.cs  # PawnIO IntelMSR.bin
│   ├── services/               # FanCurveService, GameProfileService, OsdService...
│   └── shell/                  # WinForms 桌面壳
├── installer/                  # Inno Setup + 构建脚本
├── docs/                       # 开发文档
│   ├── dev-api.md, dev-backend.md, dev-frontend.md, ...
│   └── archive/                # 已归档的历史文档
└── vite.config.js              # Vite 配置
```

## 参考项目

- [PawnIO](https://github.com/namazso/PawnIO.Setup) — 内核驱动（EC IO / SMU / MSR）
- [BellatorFanControl](https://github.com/Aveare/BellatorFanControl) — 风扇控制参考
- [UXTU](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility) — 通用调优工具
- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) — AMD SMU 参考协议
- [NvAPIWrapper](https://github.com/Orhl/NvAPIWrapper) — NVAPI .NET 封装参考

## 许可证

[GNU General Public License v3.0](LICENSE)
