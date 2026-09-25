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
- **PowerShell 工具会话无法启动任何外部 exe**（`git`、`dotnet` 都返回空输出且 `$LASTEXITCODE` 为空），
  所以 `deploy.ps1` / `start-dev.ps1` / `build-installer.ps1` / `sync-repos.ps1` 在这里都跑不了；
  但 `[Parser]::ParseFile` 可以做语法校验，`Get-CimInstance` / `Get-Command` 可做探测。
  需要输出时把结果 `Out-File` 到 `$env:TEMP`，再用 Read/cat 读（工具不捕获 stdout）。

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

## 本工具环境无法执行任何需要 NuGet 的 dotnet 操作（2026-09-25 实测，重要）

- 症状：`dotnet restore`、`dotnet publish -r <rid>`（**即使带 `--no-restore`**）、`dotnet nuget list source`
  一律报 `Value cannot be null. (Parameter 'path1')`；堆栈是
  `NuGet.Configuration.XPlatMachineWideSetting..ctor()` → `NuGet.Common.NuGetEnvironment.GetFolderPath()`
  → `Path.Combine(null, ...)`。连读取**已存在**的 `obj/project.assets.json` 都会报（`NETSDK1060`）。
- 已排除的嫌疑：沙箱（`dangerouslyDisableSandbox` 下能成功写 `C:\ProgramData`，NuGet 依旧失败）、
  环境变量（补齐 `APPDATA`/`ProgramData`/`ALLUSERSPROFILE`/`TEMP` 等仍失败，且已用 node 验证变量确实传进了子进程）、
  仓库配置（仓库根无 `global.json`/`Directory.Build.props`；在 `C:\` 下执行同样失败）→ **环境级问题**。
- **`dotnet build --no-restore`（不带 RID）是可用的**，因为这条路径不经过 NuGet 设置求值。
  → 所以「验证 C# 改动能否编译」可以用它，但**发布（publish）不行**。
- 后果：`installer/build-installer.ps1` 的 `[3/6]`/`[4/6]` 两次 `dotnet publish -r win-x64` 跑不了
  → **在本工具里无法重打安装包**。需让用户在自己的终端里跑该脚本。
- 相关：`microsoft.web.webview2` 不在本地 NuGet 缓存（Shell 项目从未还原过，其 `obj/project.assets.json` 不存在）；
  VS2022 Community 的 `MSBuild.exe` 存在，但**被安全策略按 LOLBin 拦截**，不能当替代方案。
- **订正**：不要把 `--no-restore` 当成这个报错的「解法」记 —— 那只是绕过 restore，真实原因是 NuGet 初始化失败。

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
