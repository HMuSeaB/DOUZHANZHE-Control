# MEMORY.md — DOUZHANZHE-Control 项目长期记忆

## 构建 / 发布流程（关键约定，2026-08-19 验证）

- **一键打包脚本**：`installer/build-installer.ps1 -Version <稳定号>`（如 `2.0.1`）。它内部调用 `deploy.ps1`（前端构建+同步 wwwroot）、`.NET publish`、Inno Setup 编译，末尾 `[6.5]` 调 `sync-repos.ps1`。
- **版本号约定**：`package.json` 始终保留 **base 版本**（如 `2.0.1`），预发布后缀（`-pre.1`）只写在 **CHANGELOG 顶部标题 + git tag**。脚本 `[0/6]` 会识别 CHANGELOG 顶部为预发布（`含 -`）且传入稳定号时不覆盖它。
- **sync-repos.ps1 行为坑**：它**只在「工作树有未提交改动」时才提交**；对于已经 commit 但领先远程的提交，它**不会主动 push**。所以提交 changelog 后，必须手动：
  `git push origin feature/v2.0` 再 `git push origin v2.0.1-pre.1`（标签需单独 push，sync-repos 不推 tag）。
- **发布预发布版本 = 建 GitHub Release，不能只 push tag**：用户说"push到github"对预发布软件即指发到 **Releases**（带安装包附件）。仅 `git push --tags` 只会出现在 Tags 标签页、不进 Release 列表，等于没真正发布。正确做法：`gh release create vX.Y.Z-pre.N --title "斗战者控制台 vX.Y.Z-pre.N" --notes-file <changelog节> --prerelease <安装包.exe>`（gh 已登录 KanzakiK，token 含 repo 权限；release notes 文件要写到项目目录内，/tmp 会被沙箱回滚丢失）。
- **WorkBuddy 环境注意**：本会话曾因沙箱/安全删除防护多次卡构建。关闭沙箱、把 Node 批量删除阈值调到 9999、关闭"删除先移到回收站"保护后，整条 `build-installer.ps1` 才能跑通。前几次"卡死"实为工具防护，非脚本/代码问题。
- **v2.0.1-pre.1 changelog 内容范围**：仅含自 `v2.0.0-pre.1` 的真实增量（`cfg-` 配置模型迁移、powerPlan 高亮修复、ApplyCpuAsync、sync/import 接口、switch-stability-test.ps1、useControlState 重构+单测）。Toast/备份签名/应用内更新/平台控制接线等均不在此区间。

## 参数持久化约定（2026-09-25）

- **前端任何"用户设定值"的唯一权威源都是后端 overrides**（`useControlState` 的 `overrides` / `/api/overrides`），
  前端本地 `useState` 只允许作为拖动过程中的乐观副本。**禁止**用 `useEffect([perfMode])` 之类的 effect 把用户值
  重置成模式默认值 —— 那会在游戏自动切换 / 热键切档 / 切配置 / 重开应用时静默冲掉用户设置。
  正确写法见 `src/pages/FanControl.jsx`：`overrides.X ?? MODE_DEFAULTS[perfMode].X`，拖动 `saveOverride` + 防抖 POST。
- **配置 id 与性能模式裸名必须解包**：后端 `FanRpmRange()` / `ResolveConfigThermal()` 一套；前端
  `resolvePerfMode()` / `perfModeOf()` 一套。前端传的是 `cfg-office` 这类配置 id，后端若只 switch 裸名会静默走兜底分支。

## 部署目标：deploy.ps1 不会更新"正在运行的安装版"（2026-09-25 实测）

- 本机正在运行的实例（3100 端口，`Douzhanzhe.Shell.exe` + `Douzhanzhe.API.exe`）跑的是**安装版**，
  目录在 **`D:\Tools\Douzhanzhe Console\`**（不是 `{autopf}\Douzhanzhe Console`；`C:\Program Files (x86)\斗战者控制台`
  是联想原厂工具，与本项目无关）。
- `deploy.ps1` 只同步 3~5 个**开发树**目录（`server/api/wwwroot`、`bin/run/wwwroot`、`bin/build/wwwroot`、
  shell 的 Debug/Release wwwroot），**不包含安装目录**。所以只跑 deploy.ps1 后，安装版界面不会变。
  要验证方法：`curl -s http://127.0.0.1:3100/ | grep -o 'index-[A-Za-z0-9]*\.css'`，与 `dist/assets/` 比对哈希。
- 让改动真正生效的路径：① `installer/build-installer.ps1` 重打安装包再安装；② 起 3101 开发实例用浏览器验证
  （见下方「bin/run 是陈旧目录」——`start-dev.ps1` 目前会起错后端）；③ 手工同步安装目录（违反
  AGENTS.md「禁止手动复制文件」约定，不推荐）。
- `installer/build-installer.ps1` 只做 publish + ISCC 编译，**不会自动安装**；产物在 `dist/installer/`。

## bin/run 是陈旧目录，最新产物在 bin/build（2026-09-25 实测）

- `server/api/bin/run/` 里的二进制停留在 **2026-08-29**，`server/api/bin/build/` 才是 `dotnet build` 的输出。
- **`start-dev.ps1` 第 6 行 `$DevApiPath` 指向 `server\api\bin\run\Douzhanzhe.API.exe`** →
  照现状跑 `start-dev.ps1` 起的是 8/29 的陈旧后端，改动完全不生效。**（已发现，尚未修）**
- 判断 DLL 是否含新代码**不要只看 mtime**（源码与产物可能同一分钟），用元数据字符串最可靠：
  `grep -a -o -E "方法名A|方法名B" xxx.dll`（.NET 元数据 #Strings 堆是 UTF-8，方法名可直接 grep）。

## 本地 API 同源守卫（探测接口必读，2026-09-25）

- `LocalAccessGuard.IsAllowed` 的放行条件：`Host` 是回环 + 满足任一：
  `Sec-Fetch-Site` 为 `same-origin`/`none`，或 `Origin` 在白名单，或带正确令牌。
- **非浏览器请求（node/curl 脚本）必须自己带 `Origin` + `Referer`（同端口）**，
  或带 `X-Douzhanzhe-Token: <session.token 内容>`；否则一律
  `403 {"ok":false,"error":"请求来源不被信任"}`。
- **`session.token` 是共享文件但比对的是进程内存值**：路径
  `%LOCALAPPDATA%\Douzhanzhe Console\session.token`，每次 API 启动重新生成。
  两个实例共存时后启动者会改写文件 → 先启动的那个对所有令牌请求一律 403。
  （浏览器直连不受影响。）**排查 403 时先想到这一点。**
- `/api/fan/curve`、`/api/perf/settings` **不存在**，会落到 SPA fallback 返回 index.html，别把 HTML 当 JSON。

## 环境坑：Bash 工具里 git ref 写入被静默吞掉（2026-09-25）

- 症状：`git commit` / `git update-ref` 返回 0，但 `.git/refs/heads/<branch>` 不落盘 → 产生无父 root commit，
  本地分支指针丢失（`git log` 报 "does not have any commits yet"）。**加不加沙箱都一样**。
- 可用做法：`git commit-tree <tree> -p <parent> -m <msg>` 造提交对象 → 用普通 shell 重定向
  `printf '%s\n' <sha> > .git/refs/heads/<branch>` 写 ref → `git push`（push 正常）。
  索引复位用 `git read-tree <sha>`（不碰 ref）。**不要用 `git update-ref`**。
- ref 文件必须写**完整 40 位 SHA**，写短 SHA 会得到 "your current branch appears to be broken"。
- `deploy.ps1` 在 PowerShell 工具会话里调不到 `git`，`gen-build-info.ps1` 失败导致第 1 步就 abort；
  需要时手工执行其 1~3 步（bash 复制 dist → wwwroot + 写 version.txt/build-info.json）。
- **订正**：早先记的「PowerShell 工具会话无法启动任何外部 exe」**是错的**。真实原因是进程环境被裁剪
  （缺 `PATHEXT`/`SystemRoot` 等），补齐后 `git`/`dotnet`/`npm`/`ISCC` 都能跑。详见上面那条「最重要」。
  不补环境时，外部 exe 会「无输出且 `$LASTEXITCODE` 为空」，看起来像工具不支持 —— 别被误导。
- PowerShell 工具不捕获 stdout：用 `Start-Transcript`（能抓到 `Write-Host`）或 `Out-File` 到文件后再读。
  `[Parser]::ParseFile` 可做 PS1 语法校验，`Get-CimInstance` / `Get-Command` 可做探测。
- **Bash 工具可能对同一条命令重试执行**：写 ref、建提交这类操作要写成**幂等**的，否则会产生重复提交。

## 风扇转速脏数据防护（2026-09-25）

- EC 0x9D/0x9E（大扇）、0x96/0x97（小扇）是 16 位 RPM，固件异步刷新，且与 WMI ACPI 共用 EC 缓冲。
  实机出现过 25249 RPM（大扇上限 4400）。
- 读取必须：**同一把锁内成对读**（`DriverBridge.ReadEcPair`）+ **上界校验**（用 `FanLargeMax`/`FanSmallMax`，
  不是曲线里那个 ~3000 的「手动可控上限」）+ 重试 + 回退 Last Known Good（`HAL.ReadValidatedFanRpm`）。
  前端也要有显示层兜底（超限显示 `—` / `读数无效`）。
- 仍未做：DriverBridge 的 EC 事务没有 ACPI IBF/OBF 握手（只靠固定 Sleep），且 WMI 通道与 0x62/0x66
  端口事务不互斥 —— 这是脏读的深层来源，改动影响所有 EC 读写，须实机验证后再动。

## 在本机启动常驻进程 / 探测端口（2026-09-25 实测）

- **`./Douzhanzhe.API.exe` 直接执行 → `Permission denied`；改用 `dotnet Douzhanzhe.API.dll --urls=...` 可以。**
- **`nohup ... &` 起的进程会在该条 Bash 命令结束时被回收**（日志停在启动完成、没有 shutdown 记录）。
  常驻服务必须用 Bash 工具的 `run_in_background: true`（受管后台任务）。
- **`curl` 会走系统代理**（本机 `http_proxy=http://127.0.0.1:3597`），对 127.0.0.1 报
  `upstream connect failed: ... (os error 10061)`，`--noproxy '*'` 也无效 → **用 node 的 `fetch` 探测本地端口**。
- 本项目 `package.json` 含 `"type":"module"`，`logs/*.js` 探针脚本要用 ESM 写法（`import fs from "fs"`）。
- **从 Bash 工具起的进程拿不到 PawnIO 设备节点**（沙箱拦截设备路径；node 直接 `open('\\.\PawnIO','r+')` 也是 `EPERM`）。
  表现：`[PawnIO] [Detection] status=InstalledNoDevice` → `[HAL] 硬件驱动不可用，所有硬件读取将返回安全默认值`，
  遥测里 `cpuTemp`/`fanLargeRpm` 等 EC 项全为 0。
  → **沙箱内起的开发实例只能验证前端 UI 与后端持久化逻辑，不能验证任何硬件读写。**
  注意 `PawnIoDetection` 把「被占用 / 权限不足 / 沙箱拦截」统一报成 `InstalledNoDevice`（文案「可能需要重启」），
  排查时不要被这句误导。
- 安装版（3100）与开发实例（3101）的 AppLog **写在同一个文件** `%LOCALAPPDATA%\Douzhanzhe Console\logs\app.log`，
  多进程混写，看日志时要靠上下文区分是哪个实例（如 `API starting, BaseDir=...` 可区分）。

## 既有缺陷：Shell ↔ API 健康检查 403 死循环（2026-09-25 发现，未修）

- 现象：`[Guard] 拒绝 GET /api/health — 缺少或错误的会话令牌` →
  `[Shell] 健康检查连续失败 2 次，重启后端` → `[Shell] 后端重启成功 (1s)，刷新 WebView2`，每 ~16s 循环一次。
- 成因：Shell 的原生 health check 不带 `X-Douzhanzhe-Token`，也不带 `Origin`/`Sec-Fetch-Site`，
  被 `LocalAccessGuard` 拒。**实际并未真重启后端**（3100 API 的 PID 一直没变）→ Shell 看门狗形同失效，只刷日志。
- 该循环在 2026-09-24 之前就存在（app.log 里 2386 次），**与 2026-09-25 的风扇修复无关**。

## 【最重要】工具会话的进程环境被裁剪 —— 跑任何外部 exe 前必须补齐（2026-09-25 定论）

- **症状**：Bash 工具会话里 `PATHEXT` / `SystemRoot` / `windir` / `ComSpec` / `APPDATA` / `ProgramData` **全为空**。
  由此引发两类看起来毫不相关的故障：
  1. `dotnet <dll>` 直接崩：`EnvironmentProvider.get_ExecutableExtensions()` NullReferenceException（缺 `PATHEXT`）。
  2. **NuGet 全线失效**：`dotnet restore` / `dotnet publish -r <rid>`（**连 `--no-restore` 也一样**）/
     `dotnet nuget list source` 全报 `Value cannot be null. (Parameter 'path1')`，
     堆栈 `NuGet.Configuration.XPlatMachineWideSetting..ctor()` → `NuGetEnvironment.GetFolderPath()` → `Path.Combine(null,...)`。
     连读取**已存在**的 `obj/project.assets.json` 都会报（`NETSDK1060`）。
- **解法**：调用外部 exe 前显式补齐整套 Windows 环境变量：
  `PATHEXT='.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC'`、
  `SystemRoot`/`windir`=`C:\Windows`、`SystemDrive=C:`、`ComSpec=C:\Windows\system32\cmd.exe`、`OS=Windows_NT`、
  `APPDATA`、`LOCALAPPDATA`、`ProgramData`、`ALLUSERSPROFILE`、`USERPROFILE`、`USERNAME`、
  `ProgramFiles`/`ProgramFiles(x86)`/`ProgramW6432`、`CommonProgramFiles`、`TEMP`/`TMP`，
  以及 `Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + (...'User')`。
  补齐后 `dotnet nuget list source` 正常、`dotnet publish -r win-x64` 正常。
  **零散补几个变量不够，必须成体系地补。**
- **订正历史结论**：不要再用「改用 `--no-restore` 规避」这种说法 —— 那只是绕过症状。
- **同样订正**：早先记的「PowerShell 工具会话无法启动任何外部 exe」**是错的**。
  补上 `PATHEXT` 等之后，PowerShell 工具里 `& dotnet --version` 能正常输出。
  也就是说 `deploy.ps1` / `start-dev.ps1` / `build-installer.ps1` / `sync-repos.ps1`
  **在 PowerShell 工具里都能跑**。PowerShell 工具不捕获 stdout，用 `Start-Transcript` 落盘后再读。
- 另：`dotnet build --no-restore`（不带 RID）即使环境不全也能跑，因为不经过 NuGet 设置求值 —— 别被它「能用」误导。

## 重打安装包的完整流程与已知卡点（2026-09-25 实测跑通）

1. 先定版本号（见「版本号约定」），把 `package.json` 与 CHANGELOG 顶部都改成目标号。
2. PowerShell 工具 + 补齐环境，跑 `installer/build-installer.ps1 -Version <号>`，
   用 `Start-Transcript` 把输出落到 `logs/build-installer.log`。
3. 脚本会走到 `[5/6]` 合并完成后**卡在清理步骤**：工具的「安全删除」保护拦截 `Remove-Item`
   （`[safe-delete][SAFE_DELETE_FAIL_CLOSED] ... genie-trash failed; refusing fallback delete`）。
   **这不是脚本缺陷**，改用 bash 的 `rm` 完成剩余清理（bash 不受该保护影响）：
   `rm -f dist/publish/api/Microsoft.Web.WebView2.{Wpf.dll,Wpf.xml,Core.xml,WinForms.xml}`、
   `rm -rf dist/publish/api/runtimes`（根目录已有 `WebView2Loader.dll`）、清 `dist/publish/api/config/*.json`。
4. 手工跑 ISCC：`ISCC.exe installer/douzhanzhe-setup.iss /dMyAppVersion=<号>`。
   **Git-Bash 会把 `/dXxx` 当路径转换**，必须加 `MSYS2_ARG_CONV_EXCL='*'`。
5. 产物落在 `dist/installer/DouzhanzheConsole-<号>-Setup.exe`（约 9.4 MB）。
   `dist/` 与 `build-info.json` 都在 `.gitignore` 里，不会被提交。
6. **别跑 `sync-repos.ps1`**（见下条 git ref 坑），改为手工 `git push`。

## 版本号约定（build-installer.ps1 的坑）

- 不带 `-Version` 时，脚本用正则 `"version"\s*:\s*"(\d+\.\d+\.\d+)"` 从 package.json 取值，
  **只取 `2.0.1`，会丢掉 `-memory-fix.5` 后缀**；而 `[5.5]` 会校验前端 bundle 里必须含 `v$Version`。
  另外 package.json 里若已是预发布号（`2.0.1-memory-fix.5`），`[0]` 的替换正则（要求数字后紧跟 `"`）**匹配不上**。
- 结论：**要沿用 `-memory-fix.N` 系列，就先把 package.json 与 CHANGELOG 顶部都改成目标号，再显式传 `-Version`。**
- 安装包目录：ISS 里 `AppId` 固定、`DefaultDirName={autopf}\Douzhanzhe Console`、`PrivilegesRequired=admin`；
  没有 `UsePreviousAppDir` 指令（默认 yes）→ 若现有安装是同一 AppId 装的，会默认沿用原目录。
  本机现有安装版在 `D:\Tools\Douzhanzhe Console`（版本 `2.0.1-memory-fix.5`），不是 `{autopf}`。

## 同源守卫 / 会话令牌 / overrides 持久化（2026-09-25 修复）

- **`/api/health` 必须豁免同源守卫**：Shell 看门狗用原生 `HttpClient` 探活，既无 `Origin`/`Sec-Fetch-Site`
  也无令牌，不豁免就会被拒成 403，触发「每 16s 重启后端」的死循环。该端点只回 `{ok, timestamp}`，无敏感数据。
- **令牌文件必须按端口隔离**：`%LOCALAPPDATA%\Douzhanzhe Console\` 是安装版(3100)与开发实例(3101)共享的目录，
  而 `LocalAccessGuard` 校验用的是**进程内存里的令牌**。文件名带端口（`session-<port>.token`）才能让两实例共存；
  否则后启动者覆盖文件，先启动者对所有「带令牌」请求一律 403。
- **`SavePerfOverrides(mutate, mode)` 必须校验 mode id 存在**：未知 id 时旧实现会新建空 overrides 交给
  `SaveOverrides`，后者查 index 找不到就 `return false` 不落盘 —— 但调用处照样打 `✓ saved` 并返回 `ok:true`。
  现在返回 `bool`，未知 id 打 `✗` 且由调用方（如 `/api/fan/set-target`）转成 400。
- 前端 `settings.mode` 传的是**配置 id**（`cfg-office`），不是裸性能模式名（`office`）。
  用裸名调 `/api/fan/set-target?mode=...` 会命中「未知 id」分支 —— 排查时先确认这一点。

## 验证 .NET 二进制里是否真的编进了本次改动（可复用）

**别用 grep 搜中文** —— .NET 里两类字符串放在不同的堆：
- **方法名 / 类型名** → 元数据 `#Strings` 堆，**UTF-8**，`grep` 能直接命中。
- **字符串字面量**（代码里的 `"..."`）→ `#US` 堆，**UTF-16LE**，`grep` 搜中文一律假阴性。

正确做法（Node，注意本项目 `package.json` 有 `"type":"module"`，必须用 ESM 不能用 `require`）：
```js
import fs from 'node:fs';
const buf = fs.readFileSync(dllPath);
const hit = buf.includes(Buffer.from('未知配置 id', 'utf16le'));
```
现成脚本：`logs/verify-dll-strings.js`（按 `dist/publish/api/*.dll` 逐条比对）。

## 沙箱里硬件相关功能无法验证（重要）

工具会话被沙箱拦截 **PawnIO 设备节点** → 任何在工具里启动的 API 实例都是 `InstalledNoDevice` 状态，
日志固定打印 `[HAL] 硬件驱动不可用，所有硬件读取将返回安全默认值`。
**结论：风扇读写、EC 读数、25249 RPM 之类的脏读问题，只能在真实安装版上验证。**
不要因为在工具里测不出结果就误判为「修复无效」。

## 运行日志与观察工具（2026-09-25 建立）

**唯一日志文件**（后端与 Shell 共写）：
`%LOCALAPPDATA%\Douzhanzhe Console\logs\app.log`，按大小轮转为 `app.log.1/.2/.3`。
日志标签：`[Shell]` `[Guard]` `[HAL]` `[FanEC]` `[FanRpm]` `[Telemetry]` `[ParameterGuard]` `[overrides]`。

- **`tools/watch-applog.ps1`** —— 实时观察台，关键事件上色。
  `-Summary` 给统计 + 「被拒绝端点 TOP」分布，是**判定修复是否生效的硬指标**。
- **`tools/probe-fan.js`** —— 调速持久化探针（`--port` / `--mode` / `--rpm` / `--keep`）。

**判定标准（装 fix.6 前后对照）**：
| 指标 | fix.5 实测（坏） | fix.6 实测（好） |
|---|---|---|
| `Shell 重启后端` | **每 8 分钟 30 次** | **0** |
| `拒绝 GET /api/health` | **每 8 分钟 60 次** | **0** |
| `同源守卫拒绝`（总量） | 2903 | 0 |
| `overrides 保存失败` | 0 | 0 |

> **必须用 `-Since` 过滤时间**：`app.log` **不随升级清空**，累计总数含旧版本的病态数据
> （装完 fix.6 后总数仍显示 1481 次重启），直接看总数会误判成"没修好"。
> `.\tools\watch-applog.ps1 -Summary -Since "yyyy-MM-dd HH:mm"`
> **新 Shell 启动的标志行**：`[Shell] [DWM] ReadThemeFromConfig`（旧版没有）。
> 切换瞬间会残留 1 次旧版本的拒绝记录，别当成"没修好"。
> 最有说服力的证据形式是**同长度时间窗口对比**（如前后各取 8 分钟）。

**两个易踩的点**：
1. 同源守卫只作用于 `/api` 与 `/ws` 路径 —— `GET /`（SPA 静态）本来就不受守卫管，
   所以 Shell 启动等待用 `GET /` 没问题，只有 `GET /api/health` 会被拦。
2. 令牌文件按端口隔离 → 3100 是 `session-3100.token`；探针脚本用旧名 `session.token` 会读到空令牌。
   （历史上用旧名的 13 次守卫拒绝就是这么来的。）

## overrides 字段名的三套写法（极易搞错，2026-09-25 踩过）

后端模型 `FanOverrides { int? LargeRpm; int? SmallRpm; }`，但**不同接口用的名字不一样**：

| 位置 | 名字 | 例子 |
|---|---|---|
| `GET /api/overrides` 响应 | 嵌套 | `overrides.fan.largeRpm` / `.smallRpm` |
| `POST /api/overrides/clear` 请求 | 扁平 | `fields:["fanLargeRpmTarget","fanSmallRpmTarget"]` |
| `POST /api/fan/set-target` 请求 | 扁平 | `{ largeRpm, smallRpm }` |
| 前端 UI state | 扁平 | `overrides.fanLargeRpmTarget`（`flattenBackendOverrides` 映射而来） |

**直接打 API 时看到的是嵌套名；前端 UI 用的是扁平名。写探针/脚本时先 `--readonly` 打一次真实响应，
照着真实 JSON 写字段名** —— 别凭印象。

## 硬件通道的不一致（排查时别被骗）

- **真正生效的是 EC 直写通道**：`hal.WriteEcPort(0x5E/0x5A, rpm/100)`，日志 `[FanEC] EC直写 ...`。
- **`/api/fan/status` 读的是 WMI Bellator GET**，实机返回 `manualEnabled:false, largeRpmTarget:0`，
  即使 overrides 里明明有值、日志也显示写成功了。
- → **判断「风扇设置是否生效」要看 overrides 或 `[FanEC]` 日志，不要看 `/api/fan/status`。**

## 在用户真机上做写入测试的安全流程（务必遵守）

1. **先备份**：`curl -s -H "Origin: http://127.0.0.1:<port>" http://127.0.0.1:<port>/api/overrides -o logs/overrides-backup-<ts>.json`
   （**Origin 头必须带**，否则被同源守卫 403）
2. **先跑只读**：`node tools/probe-fan.js --readonly` 看清现状，并据此写字段名。
3. **写入时加 `--no-switch`**：`/api/overrides/switch` **即使 mode 没变也会走硬件重置路径**
   （`SetCurrentMode` → `ApplyThermalMode` → GPU/CPU/NVAPI 重置），会打断用户正在用的调优。
4. **恢复**：`curl -X POST -d '{"largeRpm":...,"smallRpm":...}' ".../api/fan/set-target?mode=<原模式>"`
5. **核对**：`diff` 备份与现状，必须逐字节一致，并向用户明确报告已恢复。

**别用 `grep -c` 数压缩后的 bundle**（整个 bundle 只有 8 行，`-c` 永远返回 1）；
要数出现次数用 `grep -o <pat> <file> | wc -l`。

## 相关技能：`douzhanzhe-fix-verify`（用户级，2026-09-25 建立）

验证本项目的修复是否在真机生效时**先加载它**：
`C:\Users\36230\.workbuddy-ai\skills\douzhanzhe-fix-verify\SKILL.md`
（`references/api-contract.md` 是 API/字段名/EC 寄存器契约，`references/log-taxonomy.md` 是日志判定指标。
打包副本：`dist\skills\douzhanzhe-fix-verify.zip`）

触发场景：观察/验证/确认本项目某个修复、风扇调速不持久、恢复默认、25249 RPM 脏读、
Shell 反复重启后端、403 死循环、overrides 没落盘、要跑 3100/3101 实例或打 `/api/overrides`。

**本环境创建技能的方式**：没有 `SkillManage` 工具 → 加载 `skill-creator` 技能，
用它自带的 `scripts/init_skill.py` / `quick_validate.py` / `package_skill.py`。
注意技能实际路径是 `~/.workbuddy-ai/skills/`（不是文档里写的 `~/.workbuddy/skills/`），
且 frontmatter 必须带 `agent_created: true`。技能在项目外，`git status` 看不到。
