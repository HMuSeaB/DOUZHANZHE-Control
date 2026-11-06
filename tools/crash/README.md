# tools/crash/ — 本机蓝屏病历与急救脚本

**这里放的不是控制台应用的功能代码**，是这台机器（Lecoo Bellator N176）在
2026-11 那次连续蓝屏排查中沉淀下来的工具链与结论。
和 `tools/` 其他脚本的区别：那些验证"应用改得对不对"，这里处理"机器为什么蓝屏"。

病历正文：`crash-report-2026-11-04.md`（含符号级证据、被排除项、当前结论与更正记录）。

---

## 一、脚本索引

| 脚本 | 作用 | 风险 | 什么时候用 |
|---|---|---|---|
| `copy-dumps.bat` | UAC 自提权，把 `C:\Windows\Minidump\*.dmp` 与 `C:\Windows\MEMORY.DMP` 复制到脚本所在目录的 `dumps\` | 零（只读+复制） | 每次蓝屏后，分析前的第一步 |
| `dedicated-dump-setup.bat` | 配置 `DedicatedDumpFile`（`D:\dedicateddumpfile.sys`，8 GB），让**完整内核转储**能真正生成 | 低（只写 `CrashControl` 注册表，需重启生效） | 需要完整转储时。**本机当前就靠它** |
| `dedicated-dump-undo.bat` | 删除 `DedicatedDumpFile` / `DumpFileSize` / `IgnorePagefileSize`，`CrashDumpEnabled` 还原为 3 | 低 | 不再需要完整转储时。**不会自动删 `D:\dedicateddumpfile.sys`**，重启后手动删 |
| `verifier-off.bat` | `verifier /reset`，清空驱动验证器设置 | 零 | **任何时候怀疑验证器开着，先跑这个** |
| `verifier-on-special-pool.bat` | 开启 Driver Verifier 特殊池，只针对 `Ld9BoxNetLwf` / `Ld9BoxSup` / `BAPIDRV64`，`bootmode=resetonbootfail` | **中**（可能拖慢开机；已用 `resetonbootfail` 兜底） | **对本机当前问题不对症，不建议运行**，理由见 README 第四节 |
| `windows-os-update.bat` | 只安装 Windows 11 版本升级 / 系统累积更新，跳过 .NET、VS、PowerShell 和全部驱动。结束时把更新暂停写回 2045-12-08 | 低（会升级系统并重启；不改驱动校验） | 要抬 WDDM / 内核版本、又不想把暂停永久打开时。先看窗口里的 `[install]` / `[skip]` |

`legacy/` 里是已被取代或不该再用的东西，**只作存档，别双击**：

| 文件 | 为什么进 legacy |
|---|---|
| `enable-kernel-dump.bat` | 只改 `CrashDumpEnabled=2` 的旧方案。**前提错误**：本机页面文件只在 D 盘，启动卷没有 pagefile，所以完整转储仍会失败（`volmgr 161`）。已被 `dedicated-dump-setup.bat` 取代 |
| `restore-minidump.bat` | 与 `dedicated-dump-undo.bat` 功能重叠 |
| `paren-name-crash-sample.bat` | 文件名含半角括号导致双击闪退的**实证样本**，见第三节第 2 条 |

### 原编号 → 现文件名对照（从会话工作区迁来时改的名）

| 原名 | 现名 |
|---|---|
| `复制蓝屏转储.bat` | `copy-dumps.bat` |
| `7-修复内核转储.bat` | `dedicated-dump-setup.bat` |
| `8-撤销专用转储文件.bat` | `dedicated-dump-undo.bat` |
| `4-关闭驱动验证器.bat` | `verifier-off.bat` |
| `3-开启驱动验证器(特殊池).bat` | `verifier-on-special-pool.bat` |
| `5-开启内核内存转储.bat` | `legacy/enable-kernel-dump.bat` |
| `6-恢复小内存转储.bat` | `legacy/restore-minidump.bat` |

---

## 二、分析流程（别再从零手写解析器）

最小可用链路：**取转储 → `cdb.exe` + 微软符号服务器 → `!analyze -v`**。

```bat
:: cdb.exe 来自 WinDbg 的 MSIX 包，复制到普通目录再跑（直接在 WindowsApps 下跑符号会异常）
:: C:\Program Files\WindowsApps\Microsoft.WinDbg_*\amd64\  ->  <某普通目录>\cdb\
cdb.exe -z dumps\MEMORY.DMP -y "srv*https://msdl.microsoft.com/download/symbols" ^
        -c "!analyze -v; q" -logo out\an.txt
```

`analyze/` 下的脚本只在"没有 cdb / 只想快速看头部"时用：

| 脚本 | 作用 |
|---|---|
| `analyze/dump-header.py` | 手工解析 `PAGEDU64` + `TRIAGE_DUMP64` 头，秒出 bugcheck 码与四个参数，无需符号 |
| `analyze/pe-index.py` | 建 `SizeOfImage → 文件路径` 索引，用于把崩溃地址归到具体驱动 |
| `analyze/recheck.py` | **只读**复核：验证器注册表、开机时间、Minidump 列表、服务 `Start`、ACLOS 日志 |
| `analyze/newdrivers.py` | 列出近期新增/改动的 `.sys`、重点目录时间、DriverStore、卸载项 |
| `analyze/try-copy-dump.py` | 测试能否不经 UAC 直读 `C:\Windows\Minidump`（结论：不能） |
| `analyze/gen/*.py` | 生成上面那些 `.bat` 的生成器。**改脚本要走生成器**，手改容易把编码写坏 |

`pe_sizes*.json` 那类索引缓存不入库（机器特定、可重新生成）。

---

## 三、三条必须遵守的坑（都实测踩过）

### 1. `.bat` 必须是 GB18030 + CRLF + 无 BOM，并加 `chcp 936`

UTF-8 写入 → cmd 按 ANSI(GBK) 解释 → 中文全乱码。
`.gitattributes` 已有 `*.bat text eol=crlf`，换行不用操心，**编码仍然要自己保证**：

```python
txt.replace('\r\n', '\n').replace('\n', '\r\n').encode('gb18030')
```

### 2. `.bat` 文件名不要用半角括号 `()`

文件名带 `(` `)` 时，只要 `%~f0` / `%~dp0` 在 `if (...)` 或 `for (...)` 块内被展开，
路径里的 `)` 会**提前闭合括号块**，cmd 解析失败 → **双击后窗口一闪而过、无任何报错、什么都不执行**。
实证样本：`legacy/paren-name-crash-sample.bat`。

提权写法改用 `goto`，不要用 `if (...)` 包 `Start-Process`：

```bat
net session >nul 2>&1
if "%errorlevel%"=="0" goto :ADMIN
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:ADMIN
```

### 3. 自提权脚本一定要写运行日志

闪退后什么都看不到，没日志等于瞎猜。本项目的写法：

```bat
set "RUNLOG=%~dp0run.log"
echo step1 >> "%RUNLOG%"
```

---

## 四、本机当前诊断结论（2026-11-06 更正版）

* 崩溃点稳定在 `nt!ExpGetPoolTagInfoTarget+0x1c5` 的 `mov eax,dword ptr [rdx]`，
  此时 `rdx=0x800000` 被当指针解引用；调用栈是纯空闲路径
  `KiIdleLoop → KiRetireDpcList → KiExecuteAllDpcs`。
* **不是**第三方驱动在自己代码里崩，也**不是** ACE / 显卡 TDR / 磁盘 / 页面文件。
* **不是**已证实的内存条故障：`0x00800000` 多点出现曾被我误判为位线污染，
  用 `ln` 逐个核对后落在四个不同内核全局变量附近，无干净基线，结论已收回。
  → **不要因为这份病历去拆机换内存。**
* 真正的 `PoolTrackTable` 内容完好（`UMDu`/`Wfra`/`IW2d`/...），坏的是遍历时用到的指针/槽位。
* 当前最可信假设：**内核 pool tracker 的竞态/边界缺陷**，被特定硬件-固件组合放大。
  外部同子系统已有公开案例，但函数与 hash 不同，不能认定同一个 bug。
* **Driver Verifier 特殊池对本机不对症**：它只监控驱动自己申请的池块，
  而这次坏在 nt 静态数据区 / pool tracker 指针上。`verifier-on-special-pool.bat` 保留但别跑。
* 尚未做、且不拆机就能做的：`mdsched.exe` 排除内存、等第二份完整转储做同地址对比、
  **更新 BIOS**（本机 `N176DRLKV2424`，官网已有 `N176DRLKV2727.exe`）、把 WER hash 反馈给 Microsoft/联想。

---

## 五、绝对不要提交的东西

| 路径 | 大小 | 原因 |
|---|---|---|
| `dumps/`（`*.dmp`、`MEMORY.DMP`） | 单份 4 GB | 转储含完整内核内存，且体积极大 |
| `cdb/` | ~200 MB | 调试器副本，用 WinDbg 自带的即可 |
| `symbols/` | 视缓存 | 符号缓存 |
| `*.reg`（`crashcontrol_backup*.reg` 等） | 小 | 系统配置快照，机器特定 |

`.gitignore` 已针对 `*.dmp` / `dumps/` / `cdb/` / `symbols/` 加了排除规则。
**迁移脚本时只迁脚本，不要顺手 `cp -r` 整个会话目录。**
