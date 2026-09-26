# tools/ — 脚本索引

**先读这个文件再动手写新脚本。** 下面每个脚本都是踩过坑沉淀下来的，重复造轮子会重踩。

配套技能：`douzhanzhe-fix-verify`（用户级 `~/.workbuddy-ai/skills/`），
里面有完整的 API 契约、日志判定指标、以及"哪些坑不能踩"。

---

## 一、验证修复是否真的生效（最常用）

按顺序跑，前两步零风险：

```powershell
cd D:\4rchive\Code\DOUZHANZHE-Control

# 1. 日志判定 —— 只看新版本启动之后的记录（-Since 必需，见下方说明）
.\tools\watch-applog.ps1 -Summary -Since "2026-09-25 13:26"

# 2. 构建产物验证 —— 确认改动真的编进了二进制（零风险）
node tools\verify-build-strings.js

# 3. API 回归测试 —— 断言式，自带备份/恢复
node tools\regression-api.js --port 3100 --mode cfg-office

# 4. 调速探针（诊断用，非断言）
node tools\probe-fan.js --readonly          # 只读，先看现状
node tools\probe-fan.js --no-switch         # 写入验证，跳过会重置调优的 switch
```

### 每个脚本的分工

| 脚本 | 类型 | 作用 | 风险 |
|---|---|---|---|
| `watch-applog.ps1` | 观察 | 实时 tail `app.log`，关键事件上色；`-Summary` 出统计与判定 | 零 |
| `verify-build-strings.js` | 断言 | 检索构建产物里的方法名/字面量，确认改动被编进去 | 零 |
| `regression-api.js` | 断言 | 9 组断言式回归测试（`[1]`–`[8]`，含 `[3b]` 被拒请求不写硬件、`[8]` 11 端点前置守卫），退出码非 0 即失败 | 改风扇设定（自动恢复） |
| `probe-fan.js` | 诊断 | 探查/写入调速设定，看落盘、钳位、切换后是否丢失 | 写入时改风扇设定 |

**`regression-api.js` vs `probe-fan.js` 的区别**：前者只报 PASS/FAIL 带退出码（放 CI），
后者输出人读的详细信息（排查用）。别互相替代。

---

## 二、构建与打包

| 脚本 | 作用 |
|---|---|
| `installer/build-installer.ps1` | 打安装包。**必须显式传 `-Version`**，否则会丢预发布后缀（见下） |
| `installer/sync_version.py` | 同步各处版本号 |
| `tools/gen-build-info.ps1` | 生成 `build-info.json`（版本+git sha） |
| `deploy.ps1` | 前端构建 + 分发到各 wwwroot |
| `start-dev.ps1` | 起开发实例（3101） |
| `sync-repos.ps1` | 分组提交并 push。**注意它可能触发 git ref 丢失，见下方"已知坑"** |

版本号约定：沿用 `2.0.1-memory-fix.N` 系列，**显式传 `-Version 2.0.1-memory-fix.N`**。
不传时脚本从 `package.json` 探测，虽然能读出预发布后缀，但保险起见还是显式传。

> 2026-09-25 修好的坑：脚本里三处版本号正则的字符类原是 `[A-Za-z0-9.]`，**不含 `-`**，
> 于是 `-memory-fix.N` 遇到第二个连字符就匹配失败 —— `package.json` 静默不更新，
> `deploy.ps1` 接着用旧号覆盖 `version.txt` 并构建出旧前端，最后在 `[5.5/6]`
> 报"前端版本号与预期不一致"。旧 README 让手工先改 `package.json` 正是被它逼出来的绕法，
> 现在不必了（详见 `installer/build-installer.ps1` 里的注释）。

构建失败的排查顺序：`[5.5/6]` 报版本号不一致 → 先看 `package.json` 是否真的被改成目标号，
没改就是上面这条；`build-installer.ps1` 中途退出且报错信息是"无法识别 Write-Warn" →
`sync-repos.ps1` 缺失分支的 cmdlet 名写错了（已修）。

---

## 三、诊断与压测

| 脚本 | 作用 |
|---|---|
| `tools/check-dll-versions.ps1` | 列出目录里各程序集的 `AssemblyVersion`；给 `-ComparePath` 则报出两个目录间的版本漂移 |
| `tools/soak-monitor.ps1` | 长时间采样进程内存/句柄，配 MemoryViewer 用 |
| `tools/start-test-api.ps1` | 单独起 API 做测试 |
| `tools/install-pawnio.ps1` | 装 PawnIO 驱动 |

---

## 四、必须知道的坑

### 1. `-Since` 不是可选项

`app.log` **不随升级清空**。装完新版直接跑 `-Summary` 会把旧版本的病态数据算进去 ——
实测装完 fix.6 后总数仍显示 `重启后端 1481`，**会误判成"没修好"**。

找"新版本启动的时刻"：日志里的 `[Shell] [DWM] ReadThemeFromConfig` 行是新 Shell 启动标志；
或看 `D:\Tools\Douzhanzhe Console\version.txt` 与 DLL 时间戳。
切换瞬间会残留 1 次旧版本的拒绝记录，别当成失败。

**最有说服力的证据形式是同长度时间窗口对比**（如前后各取 8 分钟）。

### 2. .NET 字符串分布在两个堆，编码不同

| 内容 | 堆 | 编码 | grep 能抓到吗 |
|---|---|---|---|
| 方法名 / 类型名 | `#Strings` | **UTF-8** | 能 |
| 字符串字面量 | `#US` | **UTF-16LE** | **抓不到中文** |

所以 `verify-build-strings.js` 的清单里 `method` 和 `literal` 分开列，分别按 UTF-8 / UTF-16LE 检索。
用 grep 搜二进制里的中文会得到假阴性。

### 3. 压缩后的 bundle 不能用 `grep -c`

`index-*.js` 压缩后只有 8~9 行，`grep -c` 数的是**行数**，永远返回 1。
要数出现次数用 `grep -o <pat> <file> | wc -l`。

### 4. 打 API 必须带 `Origin`

同源守卫只作用于 `/api` 与 `/ws`。除 `/api/health`（豁免）外，
不带 `Origin: http://127.0.0.1:<port>` 一律 403。令牌文件是 `session-<port>.token`。

### 5. 写入测试必须备份 + 恢复

`regression-api.js` 已内置。手写脚本时照抄这个流程：备份 → 测试 → 恢复 → `diff` 核对逐字节一致。
**跑完必须向用户明确报告已恢复。**

### 6. `/api/overrides/switch` 会重置硬件

**即使 mode 与当前相同**，它也会走完整重置路径（GPU / NVAPI / CPU 电源方案）。
只想验证"值是否持久"就加 `--no-switch`。

### 7. 工具会话环境被裁剪

在 Bash/PowerShell 工具里跑外部 exe（dotnet/npm/ISCC）失败时，先补环境变量
（`PATHEXT`/`SystemRoot`/`APPDATA` 等）—— 见 `tool-env-external-exe` 技能。
PowerShell 工具跑的是 **pwsh 7**（不是 5.1），且**不捕获 stdout**，
用 `& script *> out.txt` 落盘再读。含中文的 `.ps1` **必须带 UTF-8 BOM**。

### 8. 非提权会话起不了 API —— 但可以用 `dotnet <dll>` 绕过

`Douzhanzhe.API.exe` 内嵌 `app.manifest` 的 `requestedExecutionLevel="requireAdministrator"`。
当前会话若在 Medium 完整性级别（`whoami /groups | grep S-1-16-8192`），
直接跑 exe 会被系统拒绝，报 `env: './Douzhanzhe.API.exe': Permission denied`。

**绕法：用 `dotnet Douzhanzhe.API.dll --urls=http://127.0.0.1:3101` 启动**，
走 dotnet host 而非 exe 的 manifest，普通权限也能起。实测能力边界：

| 能力 | 非提权实例 | 说明 |
|---|---|---|
| HTTP 层（守卫 / overrides / 钳位 / 令牌） | **可用** | 回归测试全部能跑 |
| `[FanEC] EC直写` 日志 | **可用** | 写入路径照常执行并打日志 |
| `GET /api/telemetry`、`/api/system/info` | **阻塞/为空** | HAL 读路径不可用，日志会打 `硬件驱动不可用，所有硬件读取将返回安全默认值` |

**别用 `start-dev.ps1` 起测试实例** —— 它会杀掉正式版进程，打断用户正在用的 3100。
（`start-test-api.ps1` 是安全的自提权版本，但它走 `dist/test-api`，需先 `-Build`。）

### 9. `verify-build-strings.js` 查的是 publish 产物，不是 build 产物

清单 `root` 指向 `dist/publish/api`。改完代码只跑 `dotnet build`（产物在
`server/api/bin/build/`）时，**publish 目录还是旧的**，验证会漏掉本次新增的字符串。
正确顺序：`installer/build-installer.ps1`（或手工 publish）→ 再跑验证。

### 10. git ref 可能静默丢失 —— 且沙箱内 bash 写不回去

`git commit` 会报告成功、提交对象也确实写进了 `.git/objects`（`git cat-file -t <sha>` 能查到），
但 `.git/refs/heads/<branch>` **不落盘**。症状：`git rev-parse HEAD` 报
`ambiguous argument 'HEAD'`，`.git/refs/heads/` 为空，分支变 unborn。

**根因（2026-09-25 实测确认）：工具里的 bash 跑在沙箱内，对 `.git/` 的写入不会持久化。**
所以旧文档里写的 `printf '%s\n' <SHA> > .git/refs/heads/<branch>` **不管用** ——
命令返回成功，文件却不存在。

**正确修法**：用文件写入工具（非 bash）直接写 ref 文件，内容为完整 40 位 SHA 加一个换行。
写完立刻验证：

```bash
git rev-parse HEAD                 # 必须回显 40 位 SHA，不能报 ambiguous
git status --short --branch        # 应显示 ## <branch>...<remote>/<branch> [ahead N]
```

确认后再 `git push`。**`sync-repos.ps1` 会触发这个问题**，重要提交建议手工 `git push` 并核对 ref。

### 11. Edit/Write 也可能静默不落盘 —— 改完必须回读

2026-09-25 实测：对 `tools/README.md` 的一次多行 `Edit` 返回「成功」，
但文件内容没变（同批次的另一处小改动却正常落盘）。
**凡是改了文件，都要用 `grep`/回读确认内容真的在**，再写提交信息 —— 否则提交信息会描述
一些并不存在改动。

### 12. 合并发布时，Shell 的程序集会覆盖 API 的 —— 版本不一致就炸

`build-installer.ps1` 的 `[5/6]` 把 Shell 的输出合并进 `dist/publish/api`，两者同目录出货。
**同名程序集只有一个能留下**。若两个 csproj 引用了同一包的不同版本，低版本可能覆盖高版本。

2026-09-26 实际踩到：`Douzhanzhe.API.csproj` 要 `TaskScheduler 2.12.2`（程序集 2.12.2.0），
`Douzhanzhe.Shell.csproj` 是 `2.11.0`（2.11.0.0），合并后装机目录只带 **2.11.0.0**。
API 启动时按 2.12.2.0 请求 → `FileNotFoundException` → `POST /api/auto-start` 返回 **500**
（`TaskService` 的类型解析失败发生在 **JIT 期**，异常**逃出方法内的 try/catch**，直接冒到全局处理器）。

**⚠️ 不能用文件大小判断程序集版本。** `"2.11.0.0"` 与 `"2.12.2.0"` 等长，
改版本号**不改变文件大小** —— 实测两者都是 334848 字节（我因此误判过一轮）。
必须读 `AssemblyName`，用 `tools/check-dll-versions.ps1`。

**排查与验证套路**：
1. `.\tools\check-dll-versions.ps1 -Path "<装机目录>" -ComparePath "dist\publish\api"` 揪漂移。
2. 要证明"某版本能加载、某版本不能"，建一个引用目标版本的最小控制台程序，
   把输出目录里的 DLL 换成低版本再跑 —— 低版本会报与线上日志**逐字一致**的
   `Could not load file or assembly ... Version=X.Y.Z.0`。这是零副作用的 A/B 证据
   （比去动真机接口安全得多：`POST /api/auto-start` 会**改写用户的计划任务**，别拿来测）。
3. `System.*` 这类**共享框架自带**的程序集，漂移通常无害（运行时从框架解析、忽略本地副本）。
   要盯的是**第三方 NuGet 包**。实测 EventLog 有 10.0.0.0 vs 8.0.0.0 漂移但零报错。

---

## 五、写新脚本的规则

1. **先查本文件**，有现成的就别新写。
2. 新增脚本要在这里登记，并说明"什么时候用、和现有脚本什么关系"。
3. 需要断言就给**退出码**（0 通过 / 1 失败），别只打印。
4. 会改用户配置的脚本必须**自带备份/恢复**。
5. 参数化端口（3100 安装版 / 3101 开发实例），别写死。
6. 脚本要**幂等** —— Bash 工具可能重试执行同一条命令。
7. 含中文的 `.ps1` 存成 **UTF-8 with BOM**。
