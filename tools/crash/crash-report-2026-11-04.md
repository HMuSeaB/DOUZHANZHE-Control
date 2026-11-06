# 蓝屏分析报告（符号级 + 二次安全复核版）— 2026-11-04 / 11-05

机器：来酷 Bellator N176 / Ryzen 9 8940HX / RTX 5060 Laptop + Radeon 610M / 32GB（Micron 16G + Sinker 16G 混插，DDR5-5200 JEDEC，未超频）/ Win11 26100.7462 / VBS-HVCI 开启
工具：WinDbg 自带的 `cdb.exe` + 微软符号服务器（`msdl.microsoft.com`），对 5 个 minidump 逐个执行 `!analyze -v`
原始输出：`out\an4_*.txt`；二次复核脚本：`tools\recheck.py`、`tools\newdrivers.py`

---

## 一、结论（符号级证据，不是推断）

**5 次蓝屏的故障模块全部是 `ntkrnlmp.exe`（Windows 内核本体），没有一次是第三方驱动在自己的代码里崩。**
内核在**遍历/访问自己的内存管理结构时读到了被写坏的指针**。这是「内核池/页管理数据被破坏」，不是游戏崩溃、不是显卡挂了、也不是某个驱动自身抛异常。

| 转储 | BugCheck | 崩溃函数（带符号） | 在做什么 |
|---|---|---|---|
| 9000（19:10） | 0x1E | `nt!ExpGetPoolTagInfoTarget+0x1c5` | 池标签统计表的通用 DPC 回调 |
| 8812（19:41） | 0x1E | `nt!ExpGetPoolTagInfoTarget+0x1c5` | 同上，同一条指令 |
| 8875（22:00） | 0xA | `nt!ExpGetPoolTagInfoTarget+0x1c5` | 同上，同一条指令 |
| 8937（19:59） | 0x50 | `nt!SepSidInTokenSidHash+0x56` ← `NtCreateLowBoxToken` | 创建低权限令牌时查 SID 哈希，读到野指针 |
| 8828（19:42） | 0xBE | `nt!MiSystemFault` → `dxgkrnl!wil_details_FeatureReporting_RecordUsageInCache` | DWM 发起合成帧时，写了一个只读页 |

三次命中的是同一条指令：

```
fffff800`b883bb85  8b02    mov  eax,dword ptr [rdx]
```

`rdx` 落在 `0xFFFFFFFFFFFF0000` 附近，读取地址报 `0xFFFFFFFFFFFFFFFF`。失败桶
`AV_nt!ExpGetPoolTagInfoTarget`，`MODULE_NAME: nt`，`IMAGE_NAME: ntkrnlmp.exe`，`IMAGE_VERSION: 10.0.26100.7462`。

完整调用栈（三次相同，自底向上）：

```
nt!KiIdleLoop -> nt!KiRetireDpcList -> nt!KiExecuteAllDpcs
             -> nt!ExpGetPoolTagInfoTarget+0x1c5     <-- 崩在这里
             -> nt!KiGeneralProtectionFault / KiPageFault
             -> nt!RtlDispatchException -> nt!KeBugCheckEx
```

另外两次（SID 哈希表、dxgkrnl 只读页）是**同一类损坏在别的子系统里显形**。

> 事件日志显示 11-04 实际至少 **6 次** BugCheck：`16:31 0x1E`（`110426-8703-01.dmp`，现不在 Minidump 目录）、
> `19:10 / 19:41 / 19:42 / 19:59 / 22:00`，外加用户照片里的 `PFN_LIST_CORRUPT 0x4D`。
> 目录里现只剩 5 个 dmp，早期那个可能已被联想蓝屏分析工具处理或清理。

## 二、为什么「一离开电脑就崩」「一开无畏契约就放大」

这是二次复核里最有用的一条，能把你两个主观感受和调用栈对上。

**1）「人离开一会儿就黑屏重启」= 崩在 CPU 空闲循环。**
三次崩溃的栈顶是 `KiIdleLoop → KiRetireDpcList`，即 **CPU 空闲时才被调度**的 DPC 处理路径。
`ExpGetPoolTagInfoTarget` 是有人调用 `NtQuerySystemInformation(SystemPoolTagInformation)` 时，
内核用 `KeGenericCallDpc` 广播到所有 CPU 去收集池标签统计的回调。
也就是说有个常驻程序（管家/安全软件/监控类工具常见）在周期性查池标签统计——这个查询长期存在且无害，
但**昨天池里的数据被写坏了**，于是查询一读就炸。人在电脑前时 CPU 忙、不进深度 idle，DPC 走别的路径，
所以你感觉「盯着就还好」。这不是玄学，栈里写得很清楚。

**2）「一开无畏契约就放大」= 游戏是放大器，不是元凶。**
- 游戏 + ACE 启动会大量创建内核对象、令牌、池分配。`8937` 那次正崩在
  `NtCreateLowBoxToken → SepSidInTokenSidHash`（低权限令牌是反作弊/沙箱常用操作），
  说明这类路径确实在被高频使用，会显著提高踩到坏结构的概率。
- `8828` 那次是 DWM 合成帧写只读页，属于 GPU/桌面合成压力下的显形。
- 关键是：**破坏一旦留下，爆发不需要和游戏运行时间重合**。游戏退出后 ACE 内核驱动卸载，
  损坏的池结构还在，之后在空闲 DPC 或桌面合成时才被引爆。
- 所以「dmp 里搜不到任何 ACE 模块」和「你明显感觉开游戏后变严重」**并不矛盾**：
  前者说崩溃瞬间它不在场，后者说它把踩雷概率拉高了。

> **一处容易误读的桶，务必避开**：`110426-9000-01.dmp` 的
> `FAILURE_BUCKET_ID` 是 `AV_tcpip!UdpSendMessages`、`IMAGE_NAME: tcpip.sys`，
> 但那**不是崩溃原因**。它的栈是：线程本来在
> `afd → tcpip!UdpSendMessages+0x411`（发 UDP 包，`PROCESS_NAME: GameViewerServ`），
> 被 `KiDpcInterrupt` 打断 → 切到 DPC 栈 → `KiRetireDpcList → KiExecuteAllDpcs`
> → 崩在 `nt!ExpGetPoolTagInfoTarget+0x1c5`。`!analyze` 只是按「被中断前的上下文」选了桶。
> **不要拿 `tcpip.sys` 当肇事者去搜或去更新网卡驱动**，真凶指令仍是那条 `mov eax,[rdx]`。
> 顺带记下：这台机器上的网络过滤栈相当拥挤（见第六节），`GameViewer` 来自
> `D:\Tools\uu\GameViewer\`，崩溃时正在发 UDP。

## 三、被排除的东西

- **ACE 反作弊 / 无畏契约：排除（证据比上版更硬）。** 5 个 dmp 中用 UTF-16 逐个搜索
  `ACE-BASE.sys`、`ACE-CORE30405.sys`、`ACE-ADVT.sys`、`ACE-BOOT.sys`、`ACE-GAME.sys`，**一个都没有**。
  服务侧 `ACE-ADVT / ACE-CORE30405 / ACE-GAME` 全是 `Start=3`（手动），`ACE-BASE / ACE-BOOT` 连服务键都没有。
  11-04 唯一被改写的内核驱动是 `ACE-ADVT.sys`（16:54），但**首次蓝屏 16:31 就发生了，在它之前**——
  时间顺序不支持「ACE 更新导致蓝屏」。游戏日志最新停在 11-04 17:31，11-05 开机后没有新日志、没有 ACE 进程。
  > 更正说明：早先用 ASCII 搜索、且只索引了 `System32\drivers`，漏掉 `C:\Program Files\AntiCheatExpert`，
  > 一度得出错误结论；此处以 UTF-16 全文检索 + 服务启动类型 + 时间顺序为准。
- **显卡驱动自身挂起：排除。** 近 7 天无 TDR（4101/4102）；0xBE 那次栈里虽是 dxgkrnl，但它是被写的一方。
- **磁盘：基本排除。** SSD（MasonSemi RC5000 1TB）SMART Healthy，分区正常；C: 剩 42GB、D: 剩 36GB。
- **开机日志里那些红字：长期背景噪音，不是新变量。** 每次开机都有
  `7026`（`aehd`、`dam` 两个 boot-start 驱动未加载）、`7000`（`HRWSCCtrl / Lenovo Windows Security Center`
  数字签名验证失败，服务 Auto 但 Stopped）、`219`（`WUDFRd` on `ROOT\DISPLAY\0000`，0xC0000365）。
  这些在 **11-03 21:20 那次正常开机**也全部存在，与 11-04 的崩溃爆发没有时间对应。

## 四、唯一剩下的问题：到底是谁写坏了内核池

栈里**清一色是 nt，没有任何第三方驱动帧**，小内存转储里**无法反推凶手**——踩雷的人和埋雷的人不是同一个。
两个可能来源：

1. **某个内核驱动越界写 / 释放后使用**，把池标签表或相邻结构写坏，很久以后才由无关查询触发。
   5 个 dmp 里**全部在场**的第三方常驻驱动：`Ld9BoxNetLwf`、`Ld9BoxSup`、`BAPIDRV64`(360)、
   `lnvscenter`(火绒)、`XunYouFilter`、`netfilter2`、`SangforVnic`、`npcap`、`wintun`、
   `ViGEmBus`、`PawnIO`、`uuycinput`、`NeSigVerify`。
2. **物理内存不稳**（Micron + Sinker 混插）。但跑 5200 JEDEC 1.1V 未超频、此前 90 天稳定，可能性略低。

**11-04 新增变量排查（含一处更正）：**

| 项 | 事实 | 判断 |
|---|---|---|
| 联想电脑管家 | `D:\Lenovo\PCManager` 创建于 **2025-06-27**，版本目录 2025-10-16；**装机就带的** | 曾误判为「昨天刚装」，**已收回**。长期常驻，不是新增变量 |
| ACE-ADVT.sys | 唯一 11-04 被改写的 .sys（16:54），晚于首次蓝屏 16:31 | 时间顺序不支持因果 |
| DriverStore | 11-01 之后**无新增驱动包** | 没有新驱动被安装 |
| FlClash 0.9.3 | **11-04 安装**，`D:\Tools\FlClash\`，`HKCU\...\Run` 开机自启，当前 `FlClash.exe` / `FlClashCore` / `FlClashHelperService` 都在跑 | 没新装 .sys，但开 TUN/系统代理会在运行时加载 `wintun.sys`（Start=3，文件 4 月就在）并做大量包过滤。**昨天唯一新增的内核侧活动**，与游戏叠加时流量放大 |

> 不能断言 FlClash 是凶手——但它**零成本可验证**（退出 + 关 TUN 观察一天），比跑验证器安全得多。

## 五、驱动验证器：当前状态与安全版 v2

**当前没启用，你还没跑过任何东西。** 2026-11-05 13:26 复核：

```
注册表 Memory Management 下 VerifyDrivers / VerifyDriverLevel / VerifyFlags /
VerifyBootMode / VerifyDriverBootMode / PoolTagOverruns  → 全部不存在
verifier /querysettings → 验证程序标志: 0x00000000，验证的驱动程序: 无
```

`querysettings` 里显示「引导模式: Persistent」是**默认值**，不代表已启用；判别看标志和驱动列表是否为空。

**v1 已被 v2 覆盖。** `3-开启驱动验证器(特殊池).bat` 现在的实际内容（1952 字节，GB18030 + CRLF、无 BOM）：

```bat
verifier /reset >nul 2>&1
verifier /bootmode resetonbootfail
verifier /flags 0x9 /driver Ld9BoxNetLwf.sys Ld9BoxSup.sys BAPIDRV64.sys
verifier /querysettings
```

| | v1（已作废） | v2（当前文件） |
|---|---|---|
| 启动模式 | `oneboot`（只对下一次开机生效） | `resetonbootfail`（**启动失败时自动禁用验证器并重启**） |
| 驱动数 | 13 个 | **3 个** |
| 失败回滚 | 无 | `/bootmode` 或 `/driver` 失败自动 `verifier /reset` 中止 |
| 交互 | 直接执行 | 先警告 + `pause`，可直接关窗口 |
| 过期 | 自动 | **不会自动过期**，测试完必须跑 `4-关闭驱动验证器.bat` |

`0x9` = 特殊池(0x1) + 池跟踪(0x8)。下一次蓝屏若为 `0xC1` / `0xC4`，会直接写出驱动 `.sys` 名字。

**残余风险（不粉饰）：** 3 个里 `Ld9BoxNetLwf` 是 `Start=1` 开机加载、`Ld9BoxSup` 是 `Start=2` 常驻，
特殊池会在**开机早期**就开始校验，有小概率造成开机阶段蓝屏。`resetonbootfail` 能自动回滚，
`4-关闭驱动验证器.bat` 或恢复环境命令行 `verifier /reset` 都能救，但**不等于零风险**。
另外本会话权限不足，`reagentc /info` 报「只能从提升的命令提示符执行」，**WinRE 是否可用我没能替你确认**。

## 六、决策：现在**不适合**运行 3 号脚本

**先说日志能确定什么、不能确定什么：**

| | 结论 | 依据 |
|---|---|---|
| ✅ 能确定 | 故障性质 = 内核池/页管理结构被写坏；故障模块是 `ntkrnlmp.exe` | 5 份 `!analyze -v` 符号级栈 |
| ✅ 能确定 | 三次崩在 CPU 空闲 DPC 的**同一条指令** | `ExpGetPoolTagInfoTarget+0x1c5` / `mov eax,[rdx]` |
| ✅ 能排除 | ACE、显卡 TDR、磁盘、开机日志里那些红字 | 见第三节 |
| ❌ 不能确定 | **谁写坏了那块内存** | 栈里没有任何第三方驱动帧；小转储不含内核内存内容 |

**日志的天花板是物理性的，不是分析不够努力。** 本机
`CrashDumpEnabled = 3`（小内存转储），`cdb` 打开时明写：
`Mini Kernel Dump File: Only registers and stack trace are available`。
即只有寄存器和栈，**被写坏的池块本身、它的 POOL_HEADER、PoolTag、相邻块都没被保存**，
`!pool` / `!poolfind` / `!poolval` 这类能追溯分配者的命令根本无从下手。
再怎么翻这 5 个文件也问不出凶手名字。

**为什么 3 号脚本现在仍不该跑（四条）：**

1. **v2 的覆盖率撑不起它的风险。** 当前 `Win32_SystemDriver` 里 **Running** 的网络/过滤驱动有
   `Ld9BoxNetLwf`、`XunYouFilter`、`netfilter2`、`npcap`、`wintun`、`SangforVnic`、`lnvscenter`、
   `NeSigVerify`、`ViGEmBus`、`Ld9BoxSup` 等。而 v2 只验 **3 个**
   （`Ld9BoxNetLwf / Ld9BoxSup / BAPIDRV64`）。真凶若在另外 10 个里，v2 完全监控不到——
   **冒一次开机蓝屏的风险，换一个很可能抓不到的结果**，不划算。
2. **有个更便宜、信息量更大的动作没做**（见下方第七节第 1 步：升级转储类型）。
   验证器是「选择性盯 3 个驱动、制造一次带名字的崩溃」；内核转储是「无差别保存整个内核地址空间」，
   下次自然崩溃就能按 PoolTag 缩小范围，且**不改变开机时的驱动校验**。
3. **零成本对照还没做。** FlClash 是 11-04 新装、开机自启，此刻 `wintun` 确实 `Running`（TUN 已加载）。
   退出它观察一天不需要重启、不碰系统设置。
4. **WinRE 状态仍未确认。** 常见路径下 `winre.wim` 都不在 C:（正常情况它在隐藏恢复分区，
   不能据此断定失效），而 `reagentc /info` 在本会话要管理员权限、跑不了。
   `resetonbootfail` 依赖恢复流程，这个前提没落实之前不该上验证器。

> 什么时候才该跑：① 两个对照都做了还在崩；② 你已用管理员命令行确认 `reagentc /info` 显示 Windows RE 为 Enabled；
> ③ 能接受一次开机蓝屏；④ 愿意等下一次崩溃。四条齐了再跑，并且把 `/driver` 列表扩到实际 Running 的那些驱动。

## 七、建议执行顺序（零风险优先）

**结论：今天别碰 3 号。** 按下面来，一次只改一个变量，否则分不清是谁：

1. **升级日志本身（推荐，零开机风险）**：跑 `5-开启内核内存转储.bat`（自提权，先备份
   `crashcontrol_backup.reg`），把 `CrashDumpEnabled` 从 `3` 改成 `2` = 内核内存转储，然后重启。
   这不改驱动校验行为，**不会造成开机蓝屏**，只是下次崩溃多写 1–4 GB 到 `C:\Windows\MEMORY.DMP`。
   页面文件 `D:\pagefile.sys` 约 7.4 GB、C: 剩 40.9 GB、D: 剩 36.1 GB，装得下。
   有了内核转储，才能跑 `!pool` / `!poolfind` / `!poolval` 看被写坏池块的 PoolTag 和相邻块，
   把范围从「13 个常驻驱动」缩到某个具体的池标签；也才能读到 9000 那份缺失的 trap frame。
   配 `6-恢复小内存转储.bat` 随时还原（原值 3）。
2. **对照 A：退出 FlClash 并关掉 TUN/系统代理**，再开一局游戏 + 离开待机一会儿。
   它是 11-04 唯一新增的内核侧活动，`wintun` 当前 Running。零重启、随时可逆。
3. **对照 B：`1-禁用雷电内核驱动.bat`**（改服务启动类型 + 网卡绑定，不删文件），配 `2-恢复雷电内核驱动.bat` 一键还原，
   观察一天。影响：期间雷电模拟器打不开。
4. **以上都没结果且还在崩，才考虑验证器**（前提见第六节末尾四条）。测试结束必须运行 `4-关闭驱动验证器.bat`。
5. **验证器也抓不到任何驱动** → 基本排除软件，转硬件：拔掉 Sinker 那条只留 Micron 跑一天，
   再用 MemTest86 U 盘启动跑满 4 轮。

新蓝屏出现后：双击 `复制蓝屏转储.bat` 取 `MEMORY.DMP` 和新 Minidump（该脚本已包含 MEMORY.DMP），
用同一套 cdb 流程复核；若是 `0xC1/0xC4`，屏幕或转储里会直接给出 `.sys` 名字。

## 八、环境备注

- `C:\Windows\Minidump` 普通用户只能列目录不能读；已通过 `复制蓝屏转储.bat` 提权复制到 `dumps\`。
- 本会话的 `cdb\` 从已安装的 WinDbg（MSIX）复制而来（140MB），只做离线分析，未改动系统；
  从 `WindowsApps` 原路径运行时符号下载受限，复制到普通目录后正常。命令：
  `cdb.exe -z <dmp> -y "srv*https://msdl.microsoft.com/download/symbols" -c "!analyze -v;q" -logo <out>`（不要加 `.reload /f`）。
- 注册表里曾有两条雷电卸载记录，实际只装一套；`leidian9`(9.1.98.0) 是绿色版残留，已删除并备份为
  `leidian9_regbackup.reg`；保留的 `ldplayer9`(9.2.0.0) 是在用的那套。
- 所有 `.bat` 均为 **GB18030 + CRLF、无 BOM、带 `chcp 936`**（UTF-8 会让 cmd 里中文乱码）。
- 本次新增成对脚本 `5-开启内核内存转储.bat` / `6-恢复小内存转储.bat`（生成器 `tools\gen_dump_scripts.py`）。
  改动前原值：`CrashDumpEnabled=3`、`DumpFile=C:\windows\MEMORY.DMP`、`Overwrite=1`、
  `LogEvent=1`、`AutoReboot=1`；`reg export` 的完整备份会落在 `crashcontrol_backup.reg`。
- 只读复核脚本：`tools\recheck.py`、`tools\newdrivers.py`、`tools\gen_dump_scripts.py`。
- 本会话权限限制：`Start-Process -Verb RunAs`、`[Diagnostics.Process]::Start`、
  `sc.exe`（被安全策略列入黑名单）、`reagentc /info` 都被拦或要求提权，
  需要管理员的操作只能由你手动跑自提权 `.bat`；查服务状态改用注册表 / `Win32_SystemDriver`。

---

## 九、2026-11-05 18:04 第七次崩溃，与转储写入失败

**时间线（今天只有两次开机）：** `13:04:36` 开机 → `18:04:36` 意外关闭 → `18:09:42~51` 崩溃后重启。
13:04 到 18:04 之间**没有任何开机事件**，即 5 号脚本写入 `CrashDumpEnabled=2` 后**从未重启生效**。

**这次崩溃和昨天是同一个东西：**

```
1001: 0x0000001e (0xffffffffc0000005, 0xfffff801bc63bb85, 0, 0xffffffffffffffff)
转储: C:\windows\Minidump\110526-8546-01.dmp  (7,334,172 bytes)
```

崩溃地址低 20 位 `3bb85`，与 11-04 那三次 `0x...3bb85` 完全一致 →
还是 `nt!ExpGetPoolTagInfoTarget+0x1c5` 的 `mov eax,[rdx]`，**根因没有变化**。

**`cdb.exe + !analyze -v` 复核（`out\an4_110526-8546.txt`）：**

```
FAILURE_BUCKET_ID:  AV_nt!ExpGetPoolTagInfoTarget
PROCESS_NAME:       System
IP_IN_PAGED_CODE:   nt!ExpGetPoolTagInfoTarget+1c5 / mov eax,dword ptr [rdx]
READ_ADDRESS:       0xffffffffffff0000 / ffffffffffffffff
System Uptime:      0 days 5:04:49.803
STACK_TEXT:  nt!KiIdleLoop+0x9e
          -> nt!KiRetireDpcList+0x43e
          -> nt!KiExecuteAllDpcs+0x692
          -> nt!ExpGetPoolTagInfoTarget+0x1c5   <-- 崩点
```

**这次比昨天更能说明问题**：栈是**纯粹的 `KiIdleLoop` 空闲路径**，没有 9000 那份的
`tcpip!UdpSendMessages` 中断上下文，`PROCESS_NAME` 是 `System` 而不是任何游戏/加速器进程。
即**崩溃时无畏契约、ACE、GameViewer 都没在跑**，而机器已连续运行 5 小时 04 分。
这直接坐实：游戏不是直接原因，只是把池破坏「放大/提前引爆」的因素；
真正的触发条件是 CPU 长时间空闲后那个池标签统计 DPC 被调度。

**但转储没写成，这是我的疏漏：**

```
volmgr 161 (18:09:44): 转储文件创建失败，因为在创建转储期间出错。
                       BugCheckProgress 为：0x00020063
CrashDumpEnabled 现为 3（13:49 时是 2，被系统自动回退）
C:\Windows\MEMORY.DMP 不存在，只有 minidump
```

根因定位：**页面文件只在 `D:\pagefile.sys`（7.78 GB），`C:\pagefile.sys` 不存在**；
`PagingFiles = {D:\pagefile.sys 0 0}`。微软文档明确要求崩溃时内存先写入启动卷的页面文件，
再提取为 `MEMORY.DMP`。上一轮我核了 pagefile 大小和磁盘剩余空间，**漏看了它所在的卷**，
因此 5 号脚本的前提不成立——即使重启也很可能再次失败。

**修复（微软官方 DedicatedDumpFile 方案）：** 已生成成对的
`7-修复内核转储(专用转储文件).bat` / `8-撤销专用转储文件.bat`（生成器 `tools\gen_dedicated_dump.py`）。
7 号在 `CrashControl` 下设置：

| 值 | 类型 | 数据 | 作用 |
|---|---|---|---|
| `DedicatedDumpFile` | REG_SZ | `D:\dedicateddumpfile.sys` | 崩溃时直接写专用文件，绕过启动卷 pagefile 限制 |
| `DumpFileSize` | REG_DWORD | `8192` (MB) | 预占约 8 GB；内核转储一般仅几 GB |
| `IgnorePagefileSize` | REG_DWORD | `1` | 手动指定大小时的配套项 |
| `CrashDumpEnabled` | REG_DWORD | `2` | 内核内存转储 |

- 前提满足：微软要求「即使用 DedicatedDumpFile，任一分区上仍须有至少一个页面文件」，
  本机 `D:\pagefile.sys` 存在。
- **代价**：预占 D: 约 8 GB（当前剩 31.6 GB → 约 23.6 GB）；文件被系统进程持有句柄，
  撤销后需重启才能删除，8 号脚本**不会自动删文件**。
- **不改驱动校验行为**，不存在开机蓝屏风险；失败会自动回滚，备份落 `crashcontrol_backup_v2.reg`。
- **必须重启**，这是本次的关键。

**当前动作顺序：** ① 双击 `复制蓝屏转储.bat` 取 `110526-8546-01.dmp` 供 cdb 复核；
② 若接受 D: 占 8 GB，跑 7 号；③ **这次务必重启**；④ 之后正常用，再崩就有完整内核转储了。
不想占空间也可以只重跑 5 号 + 重启赌一次，但 `volmgr 161` 已证明该路径会失败，不建议。

> 若完整内核转储再次失败，剩余路径就是 Driver Verifier（扩 `/driver` 列表到实际 Running 的驱动）
> 或直接转硬件排查（拔 Sinker 那条 / MemTest86）。

---

## 十、2026-11-05 19:24 第八次崩溃，以及 7 号脚本闪退的真正原因

**19:24:51 又崩一次**（19:33:35 重启）：

```
1001: 0x00000050 (0xffffab87ba1cd016, 0x2, 0xfffff806551902d8, 0x2)
转储: C:\windows\Minidump\110526-8406-01.dmp  (6,081,918 bytes)
volmgr 161: 转储文件创建失败，BugCheckProgress 0x00000063
volmgr 162: 成功生成备份文件
```

仍是内存破坏类（0x50 写非法地址），`CrashDumpEnabled` 保持 3，只有 minidump。
这是同一根因的第四种 BugCheck 表现（0x1E / 0xA / 0xBE / 0x50）。

**7 号脚本「啥都看不到就没了」——是我写的脚本有 bug，不是操作问题。**

原文件名 `7-修复内核转储(专用转储文件).bat` 含**半角括号**，而提权段写成：

```bat
if not "%errorlevel%"=="0" (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
```

`%~f0` 在**括号块内**展开成含 `)` 的完整路径，那个 `)` 把 `if (...)` 块提前闭合，
cmd 解析直接失败 → 脚本在显示任何内容之前就退出，表现为窗口一闪而过。
`5-开启内核内存转储.bat` 文件名无括号，同样的写法就能正常跑——两者对照正好坐实。
（此前推测的「UAC 被取消」不是主因。）

**v2 修复**（`tools\gen_dedicated_dump.py` 已重写）：

1. 主用文件名改为 **`7-修复内核转储.bat`**（无括号）；旧文件也覆盖为同样的安全内容，防误点。
2. 提权逻辑改成 `goto :ADMIN` 版，`%~f0` 不再出现在任何括号块内。
3. 写 `tools\7_run.log` 运行日志（`ADMIN OK / backup / add1..4 / DONE OK` 或 `ROLLBACK`），
   即使窗口再闪退也能从日志定位。
4. 提权成功后顺手 `copy C:\Windows\Minidump\*.dmp -> dumps\`，一次运行即可取到
   `110526-8406-01.dmp` 供 cdb 复核。

**顺序提醒**：20:29:11 有一次正常开机（无 41/1001），但当时注册表四个值仍未写入，
那次重启对转储设置无效。**必须先把值写进去，再重启**，顺序不能反。

**当前动作**：双击 `7-修复内核转储.bat` → UAC 点「是」→ 按任意键 →
看到 `[OK] 已设置` 与三行 `reg query` → 重启。跑完由我读 `7_run.log` 和注册表确认。

**20:37 执行结果：成功。** 四项全部写入并复核：

```
CrashDumpEnabled   = 2
DedicatedDumpFile  = D:\dedicateddumpfile.sys
DumpFileSize       = 8192
IgnorePagefileSize = 1
```

`tools\7_run.log`：`ADMIN OK → copied minidumps → backup → add1 → add2 → add3 → add4 → DONE OK`；
`crashcontrol_backup_v2.reg` 已生成（3494 bytes）；`dumps\` 新增 `110526-8406-01.dmp`。
`D:\dedicateddumpfile.sys` 尚未生成属预期（重启后创建）。**剩最后一步：重启。**

> 安全性：这 4 个值只在**蓝屏发生之后**才被读取，用于决定「转储写到哪、写多大」，
> 不参与启动流程、不加载或校验任何驱动，与 Driver Verifier 无关，**不会造成开不了机**。
> 最坏情况只是完整转储仍写不出来（回到现状）；已有备份，`8-撤销专用转储文件.bat` 可一键还原。

### 19:24 那次 0x50 的符号级结论（`out\an4_110526-8406.txt`）

```
FAILURE_BUCKET_ID: AV_dxgkrnl!CEndpointResourceStateManager::PrepareIncrementalUpdateForStateManager
PROCESS_NAME:      dwm.exe
WRITE_ADDRESS:     ffffab87ba1cd016
IP:  dxgkrnl!CEndpointResourceStateManager::PrepareIncrementalUpdateForStateManager+57c
     adc byte ptr [rax-7Bh],cl
栈:  KiSystemServiceCopyEnd → NtTokenManagerCreateFlipObjectTokenHandle
  → FlipManagerCreateProducerTokenInitInfo → CFlipManager::CreatePresentUpdateForProducer
  → CEndpointResourceStateManager::PrepareIncrementalUpdateForStateManager+0x57c
```

与 11-04 的 `8828 (0xBE)` 同源：**DWM 翻转/合成路径写到了已损坏/只读的结构**。
这是同一个内核内存破坏在图形子系统的又一种显形，不是新的独立故障，也不是显卡驱动自身 bug
（近 7 天无 TDR）。至此 6 份转储覆盖 4 种 BugCheck（0x1E / 0xA / 0xBE / 0x50），
全部指向内核池/页管理结构被写坏，栈里始终没有第三方驱动帧——
**完整内核转储仍是唯一能定位分配者的手段**，这也是本次修复的意义。

---

## 十一、2026-11-05 22:21 第九次崩溃：完整内核转储到手，看到被写坏的字节

**配置生效后第一次蓝屏，成功写出 `MEMORY.DMP` 3.95 GB**（4,239,480,828 bytes / 22:33:49），
`dumps\` 里另有 `110526-36125-01.dmp`。`volmgr 161` 仍报 `0x00020063`，但 162「成功生成备份文件」、
1001 明确指向 `C:\windows\MEMORY.DMP` —— 专用转储文件方案奏效了。

```
1001: 0x0000000a (0x800000, 0x2, 0x0, 0xfffff801ae23bb85)
System Uptime: 1:51:13    Debug session: 22:32:18
Kernel Bitmap Dump File: Kernel address space is available
FAILURE_BUCKET_ID: AV_nt!ExpGetPoolTagInfoTarget
PROCESS_NAME: System   栈: KiIdleLoop → KiRetireDpcList → KiExecuteAllDpcs → ...+0x1c5
```

### 反汇编还原了崩溃机制（`out\full_02.txt`）

```
+0x161  mov  rdx, qword ptr [r9]      ; 从 PoolTrackTable 槽位读指针
        test rdx, rdx
        jne  +0x1bd
        add  r9, 8                    ; 逐槽遍历 0x7FF 项
+0x1bd  mov  rcx, qword ptr [rsi]
+0x1c5  mov  eax, dword ptr [rdx]     ; <-- 崩点，rdx 被当成内核指针解引用
```

陷阱帧（`.trap ffff9c80d518ef10`，`out\full_03.txt`）：

```
rdx=0000000000800000   r9=fffff801aecfd048   r10=00000000000002d7
rip=fffff801ae23bb85   rsi=0 (陷阱帧未保存该寄存器)
```

**`rdx = 0x800000` 不是随机垃圾**，而是从 `PoolTrackTable` 槽位里读出来的值。

### 决定性证据：被写坏的具体字节（`out\full_03.txt` / `full_04.txt`）

`ln fffff801aecfd048` → 落在 **`nt!ExPoolTagTables`** 数组内部
（符号区间 `nt!ExPoolTagTables+0x2948` ~ `nt!PoolTrackTableMask`，即 **nt 内核镜像的静态数据区**）。

污染范围 `aecfd048`–`aecfd078`，共 56 字节，前后全 0：

```
fffff801`aecfd040  00000000`00000000 00000000`00800000
fffff801`aecfd050  00800000`00000000 00800000`00800000
fffff801`aecfd060  00800000`00000000 00800000`00800000
fffff801`aecfd070  00800000`00800000 00800000`00000000
fffff801`aecfd080  00000000`00000000 00000000`00000000   <-- 边界干净
```

连续多个槽位被**同一个 32 位常量 `0x00800000`** 按 4 字节粒度交替填入高低半部。

> **此处原判断已作废**（详见第十二节）：当时据"整齐常量 + 干净边界"排除了内存硬件问题。
> 后续全范围搜索发现污染**不止一处**，且是**单 bit 稀疏置位**散布在多个 16KB 区域，
> 形态反而更符合内存位线/信号完整性故障。以第十二节为准。

### 被排除的与仍不能确定的

| | 结论 |
|---|---|
| ✅ 排除 | 内核镜像被篡改/中毒：`!chkimg -d nt` 的 16 处差异全是 CFG 正常自修补<br>（`_guard_check_icall_fptr`、`KscpCfgDispatchUserCallTarget*`）＋「Page 102 not present」缺页误报 |
| ✅ 排除 | 内核代码自身 bug：26100.7462 通用版本，且该机此前 90 天稳定 |
| ✅ 排除 | 游戏 / ACE / FlClash：崩溃时为 `System` 空闲线程，纯 `KiIdleLoop` 路径 |
| ❌ **仍不能点名** | **谁写了那 56 字节**。转储是死后快照，只留结果、不留写入者调用栈 |

### 一处必须更正的旧判断

此前把「Driver Verifier 特殊池」当作终极武器，**对这类破坏不对症**：
特殊池 (`0x1`) 只监控被验证驱动经 `ExAllocatePool` 申请的池块越界；
而这里被破坏的是 **nt 镜像的静态数据区**，不是任何驱动自己申请的池分配。
`3-开启驱动验证器(特殊池).bat` 的 `0x9`（特殊池+池跟踪）**很可能抓不到**这个写入者。
若真要走验证器，需换成 DMA 检查 (`0x100`) 等类别并覆盖对应设备栈，而不是继续用 v2。

### 方向变化与建议

按「固定常量覆写内核静态数据区」这一形态，最典型的两类成因：

1. **设备 DMA 越界写** —— 本机双显卡 `nvlddmkm` + `amdkmdag` 同时在场、
   雷电虚拟化 `Ld9BoxSup`（VirtualBox 派生）、VBS/HVCI 开启，DMA/IOMMU 路径复杂；
   且多次崩溃落在 DWM/`dxgkrnl` 合成路径与 CPU 空闲 DPC，与显示子系统活跃度相关。
2. **某个内核驱动持有错位/野指针**，把一块 32 位数组写到了错误的内核虚拟地址。

**仍然只是排序，不是结论**——这次不再把时间巧合当因果。

崩溃时完整模块表里确认在场的第三方内核驱动（`out\full_01.txt`）：
`Ld9BoxNetLwf`、`Ld9BoxSup`、`lnvscenter`、`XunYouFilter`、`netfilter2`、`npcap`、
`wintun`、`SangforVnic`、`SangforDnsDrv`、`SangforTcpDrv`、`ViGEmBus`、`PawnIO`、
`BAPIDRV64`、`NeSigVerify`、`uuycinput`、`uuycinputmf`、`nvlddmkm`、`amdkmdag`。
**ACE 系列依旧一个都没有。**

黑盒记录（`!blackboxbsd`，`out\full_05.txt`）：`Last boot shutdown: FALSE`、
`User/System shutdown in progress: FALSE`、有 Connected Standby 状态条目
（`Scenario Instance Id 0x10`）——与「人离开一会儿就崩」的空闲/待机触发条件一致。
`!blackboxpnp` 里 `PnpProblemCode 24` 对应一个 HID 蓝牙设备（`VID&023554_PID&fa07`），
属设备启动/停止类问题，与本次内核内存破坏无直接证据关联。

### 最省事、可逆的下一步（不急于执行）

按机制排序，**第一个对照对象是雷电的虚拟化层**：
`1-禁用雷电内核驱动.bat` 把 `Ld9BoxSup`（虚拟化支持驱动）和 `Ld9BoxNetLwf`
从开机加载里摘掉，观察一天；`2-恢复雷电内核驱动.bat` 一键还原。
它是本机唯一做 DMA/内存映射虚拟化的第三方组件且常驻。
若摘掉后不再崩，方向就锁定；若照崩，再转向双显卡其一或物理内存（MemTest86 / 拔 Sinker 那条）。

> 用户已明确「别搞麻烦、别把电脑搞坏」，以上均为可选，**当前无需任何操作**。
> 转储与全部分析输出已保存在本目录，随时可继续追查。

---

## 十二、关于「是不是内存条问题」：证据不足，不作为结论（2026-11-06 12:25 修正）

用户问「所以是内存条问题吗」。为此在完整转储里对 nt 静态数据区做了常量搜索
（`s -d fffff801aecf0000 fffff801aed00000 0x00800000`，`out\full_06.txt`），
得到 30 处命中，曾据此推断「内存位线故障」。**该推断已推翻，理由如下。**

### 命中点的实际归属（`ln` 逐个核对，`out\full_07.txt`）

```
aecf1040 → nt!MiPinDriverAddressLog+0x6e0
aecf50c4 → nt!CmpUnsupportedOperationHits+0x64
aecf90c0 → nt!WheapDeferredEventBuffer+0x720
aecfd048 → nt!ExPoolTagTables+0x2948     <-- 崩溃点
```

四处落在**四个互不相关的内核全局变量**附近，不是同一数组被涂抹。
`0x00800000` = 8 MB，本身极可能就是这些结构里的正常字段（标志位 / 尺寸值）。
**没有干净基线（同版本健康机器的转储）作对照，无法区分「正常字段」与「被写坏」。**
把「同一常量多点出现」当成「被同一股力量写坏」，属过度推断。

若真是位线 stuck-at-1，应散布在整个物理内存，不会规整地只出现在 nt 数据段
64KB 范围、四个簇、间隔几乎均为 16KB——**这个规律性本身更像结构固有内容**。

### 站得住的与站不住的

| | 内容 |
|---|---|
| ✅ 确凿 | 崩溃点 `aecfd048` 的表项是 `0x800000`，被当指针解引用 → 该处是非法值 |
| ✅ 确凿 | 9 次崩溃、5 种 BugCheck，故障模块全是 `ntkrnlmp.exe`，栈里无第三方帧 |
| ✅ 确凿 | 两条内存为不同厂商不同颗粒混插（Micron `MTC8C1084S1SC56BD1` / Sinker `SCD5S56A46PX7`，均 SPD 5600，跑 5200） |
| ⚠️ **站不住** | 「30 处 0x00800000 = 内存位线故障」——无基线，可能是正常字段 |
| ⚠️ **站不住** | 「内存是首要嫌疑」——由此推出，一并收回 |
| ✅ 补充 | 30 天无 WHEA 事件**不能**用于排除内存（本机非 ECC，DDR5 on-die ECC 不上报 OS） |

**当前结论：内存仍是候选之一，但不是首要嫌疑，也没有足够证据支持拆机更换。**

### 现场新情况（2026-11-06 12:24）

从 11-05 22:21 那次之后**连续 13 小时未再崩溃**（此前是 2–3 小时一崩）。
11-06 12:20:04 为正常关机后开机，无 `41`/`6008` 异常关机记录。
样本尚少，不足以断言已好转，但说明并非「必崩」。

### 不拆机的验证路径（按省事排序）

1. **`Win+R` → `mdsched.exe` → 立即重启检查**：不动硬件、不装软件、不改设置。
   （间歇性/边缘性问题可能漏检，未报错≠排除。）
2. **再观察两天**：若再崩，拿到第二份完整转储后做**同地址对比**——
   反复坏在 `aecfd0xx` 同一处 = 软件写固定位置；每次地址不同 = 更偏硬件。
   **这个对比比拆机更有信息量。**
3. **MemTest86 U 盘启动**：不用拆机，比 mdsched 严格。

**拆内存留到有实证再说**（两份转储对比指向随机地址，或硬件测试报错）。
为证据不足的推断拆笔记本，收益不抵风险。

---

## 十三、外部线索：同子系统已有公开案例，指向内核 pool tracker 而非硬件

2026-11-06 检索发现 Microsoft Learn Q&A 上有一组仍在进行中的报告，
故障机理与本机**高度同族**（同一套 pool tracker 机制，非同一故障桶）：

- 标题：*Recurring 0x1E bugcheck in nt!ExpPoolTrackerChargeEntry on Windows 11 25H2 (26100.8655), not fixed by KB5094135*
- 他们的故障桶 `AV_nt!ExpPoolTrackerChargeEntry`，WER hash `6f13343d-8edf-14f9-0269-6df067c74f57`；
  本机是 `AV_nt!ExpGetPoolTagInfoTarget` / `{a6a4537f-…}`。**函数与 hash 都不同，不能直接认定同一 bug。**
- 关键相似点：**每次都崩在 pool tracker 表的同一类访问上、各种不相干进程触发、
  BugCheck 种类散（0x1E/0x3B/0x23/0x135，本机 0x1E/0xA/0xBE/0x50）**。
- 报告者已排除硬件：Windows 内存诊断跑两轮通过、Dell ePSA 通过、NVMe SMART 干净；
  该机在 Win10 与 Linux 上长期稳定。
- 报告者的判断：**每次垃圾指针值都不同 → 软件竞态，不是 bit-flip**
  （bit-flip 会给散乱但"看似合理"的地址）。本机同理：`rdx` 先后是
  `0xFFFFFFFFFFFFFFFF`、`0xFFFFFFFFFFFF0000`、`0x800000`，**每次都不一样**。
- 社区专家在帖中给出架构事实：多处理器系统**每个 CPU 各有一张 pool tracker 表**，
  `PoolTrackTable` 只是 CPU0 的那张；表满时用全局 expansion 表；
  并用 `!poolused` 验证过计数正常。**与本机 `!poolused` 观察一致。**
- 该缺陷在 `KB5094135`（26100.8655）后仍复现，**微软尚未给出修复**。

### 本机与帖子的差异（必须说清，避免套用）

| | 公开案例 | 本机 |
|---|---|---|
| 故障函数 | `ExpPoolTrackerChargeEntry+0x40` | `ExpGetPoolTagInfoTarget+0x1c5` |
| 触发场景 | 约 70 万次/秒的内核 registry/file 事件突发 | **CPU 空闲 DPC**（`KiIdleLoop` 路径） |
| 表扩展 | `PoolTrackTableExpansion == 0`（未使用） | `PoolTrackTableExpansion == 0`（同样未使用） |
| WER hash | `6f13343d-…` | `{a6a4537f-…}` |

`PoolTrackTableExpansion` 两边都是 0，说明**尚未走到"扩展表替换旧表"的路径**，
所以帖子里那条 use-after-free 推断**不能照搬到本机**。

### 本机的独立佐证（`out\full_08.txt`）

```
nt!PoolTrackTable      = ffffc18b`c1602000        <- 真正的表在池内存，不在 nt 静态区
nt!PoolTrackTableSize  = 0x1001
nt!PoolTrackTableExpansion     = 0
nt!PoolTrackTableExpansionSize = 0
池表内容: "UMDu" / "Wfra" / "IW2d" / "ObCI" / "TCPT" / "HidP" / "TcST" ... 均正常 tag 与计数
```

**表本身完好无损。** 所以第十一节「池标签统计表被写坏」也不准确——被破坏的是
**遍历时用到的那个指针/槽位**，不是表内容。`ExPoolTagTables` 区域里的
`0x00800000` 更可能是未使用槽位的填充标记，而非"被写入的垃圾"。

### 因此，当前最可信的假设（仍非定论）

**内核 pool tracker 机制在特定条件下的竞态 / 边界处理缺陷**，
被特定硬件-固件组合放大。理由：同一内核指令反复命中、每次垃圾值不同、
表内容完好、别的机器同症状且硬件检测通过、KB 未修复。
**这也能解释「换内存条大概率没用」——所以更不该拆机。**

反方：本机故障函数不同、触发路径不同（空闲 DPC vs I/O 突发），
不能证明是同一个 bug；且本机此前 90 天稳定，存在某些未识别的触发变量。

### 现在真正值得做的四件事（都不拆机）

1. **`mdsched.exe` 跑一次内存诊断**。目的不是"证明内存坏"，而是**排除**：
   通过则进一步支持"内核缺陷"方向，避免白拆机。成本 20 分钟。
2. **保持转储配置，等下一次崩溃后做同地址对比**：
   - 崩溃点地址每次都变、垃圾值每次不同 → 竞态（软件），继续等修复；
   - 反复坏在同一地址 `aecfd0xx` → 固定缺陷/局部硬件，那时再拆。
3. **更新 BIOS / 芯片组驱动**（联想官网查 Bellator N176 最新版）。
   公开案例里多位报告者提到问题与特定 CPU 型号-固件组合相关，固件更新是最便宜的尝试。
4. **把 WER hash 和转储反馈上去**：本机 `{a6a4537f-27e9-ee55-8659-725433b04256}`、
   `AV_nt!ExpGetPoolTagInfoTarget`，可直接追加到上述 Microsoft Q&A 帖，或走联想售后。

在拿到第二份转储或内存诊断结果之前，**不建议更换任何硬件**。

---

## 十四、2026-11-06：联想官网 BIOS 更新（用户问「这更新应该不会出什么岔子吧」）

### 先纠正一个前提

用户截图**不是 Windows 更新界面**，而是联想知识库页面
`https://iknow.lenovo.com.cn/detail/429447`，
标题「斗战者 战7000 锐龙版（斗战者N176）驱动、预装软件、BIOS合集」。
页面下半部分是 BIOS 列表，所以这里讨论的是**刷固件**，不是装系统补丁。

### 本机实测（只读，未做任何改动）

| 项 | 实测值 |
| --- | --- |
| 厂商 / 机型 | `Lecoo` / `Bellator N176`（对应官网「斗战者N176」） |
| 当前 BIOS | `N176DRLKV2424` |
| BIOS 日期 | `2026/05/09` |
| AMD Radeon 610M | `32.0.21038.6`（2026/4/16） |
| NVIDIA RTX 5060 Laptop | `32.0.15.9247`（2026/4/22） |
| AMD SMBus | `5.12.0.44` |

对照官网列表：

* BIOS 三项：`N176 V2424` / `N176_V2525.exe` / `N176DRLKV2727.exe`
  本机正是第一项，**至少落后一版**。
  建议只取 `N176DRLKV2727.exe`——它与本机 BIOS 同 `N176DRLKV` 前缀，匹配最确定；
  `N176_V2525.exe` 命名前缀不同、适用配置不明，不要混刷。
* 驱动**一个都不用动**：独显 `32.0.15.9247` 与官网 `NV_DGPU_32.0.15.9247.exe` 完全一致；
  集显 `32.0.21038.6` 比官网 `02AMD_VGA_32.0.21030.25003.exe` 更新。

### 为什么这条线比之前所有动作都更对症

联想官方针对**同一机型**有两条公开知识库：

* `noteid=440512`：N176 独显直连模式下 S3 睡眠唤醒延迟 15 秒，
  **「更新独显驱动无法解决」**，需刷 `V2424` 版 BIOS；
* `noteid=430669`：安静模式切功耗后独显功耗卡 30W，需刷 `V1717` 或更新版 BIOS。

即：**这台机器的睡眠 / 电源状态 / 待机类问题，联想历史上一直是用 BIOS 修的，不是用显卡驱动修的。**

而本机的崩溃特征（`out/full_01.txt`、`out/full_05.txt`）：

* 纯空闲路径 `KiIdleLoop → KiRetireDpcList → KiExecuteAllDpcs → ExpGetPoolTagInfoTarget+0x1c5`；
* 「人离开电脑一会儿」才崩；
* 黑盒记录里有 Connected Standby 状态、无正常关机；
* 混插 DDR5、VBS/HVCI 开启。

固件层（AGESA / 电源状态切换 / 内存训练）与触发条件高度重合。
因此刷到最新 BIOS 是**目前唯一一个既不拆机、又直指触发条件**的动作，
比 Driver Verifier 特殊池（机制不对症）和换内存条（证据不足）都合理。

### 「会不会出岔子」的直接回答

1. 若指的是 **Windows Update 弹的普通累积更新**：可以装。
   它不加载内核驱动、不改启动配置，不会像 Driver Verifier 那样导致开不了机。
   （本次尝试用 WU COM 接口枚举待装更新被安全策略拦截，未绕过，故未列出具体 KB。）
2. 若指的是**刷这个 BIOS**：正常执行会自动重启进桌面，不会开不了机。
   **唯一实质风险是刷写那几分钟内强制断电 / 强制关机**，官网原文：
   「以上动作将导致您的设备出现无法正常使用情况」。
   硬前提：接原装充电器通电、电量 > 30%、关闭其他程序、
   中途不碰电源键不合盖、等它自己完成两轮重启。
3. 不影响既有配置：`DedicatedDumpFile` 是注册表项、不在固件里，
   刷完专用转储仍有效，第二份完整转储的同地址对比照常可做；
   Driver Verifier 仍保持关闭（BIOS 更新不会启用它）。
4. 刷 BIOS 会重置 BIOS 设置（EXPO / 内存超频、独显直连模式等）。
   若之前手动开过 EXPO，被重置反而顺带排除了内存超频这个变量，是好事。
5. 页面末尾「显示 `Lecoo N176DRLKV1616` 即可确认升级成功」是**旧版截图文案**，
   别照着对；刷完应显示 `N176DRLKV2727`。

### 诚实边界

官网**没有列出 V2525 / V2727 的更新日志**，无法确认它修了什么，
所以这是「性价比最高的一次尝试」，**不是已证实修复**。
刷完不再崩 → 固件层致因的强证据；仍崩 → 转储对比不受影响，继续按第十三节走。
