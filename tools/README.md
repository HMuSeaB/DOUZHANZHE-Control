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
| `regression-api.js` | 断言 | 5 项既有缺陷的回归测试，退出码非 0 即失败 | 改风扇设定（自动恢复） |
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

版本号约定：要沿用 `2.0.1-memory-fix.N` 系列，**先把 `package.json` 与 CHANGELOG 顶部都改成目标号，
再显式传 `-Version`**。不传时脚本用正则只截 `\d+\.\d+\.\d+`，会丢掉 `-memory-fix.N` 后缀。

---

## 三、诊断与压测

| 脚本 | 作用 |
|---|---|
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

### 8. git ref 可能静默丢失

`git commit` 可能报告成功但 `.git/refs/heads/<branch>` 不落盘（分支变 unborn，
`git status` 把所有文件显示成 `A`）。修法：用 `git commit-tree` 提交，
再用 `printf '%s\n' <完整40位SHA> > .git/refs/heads/<branch>` 幂等写 ref。
**`sync-repos.ps1` 会触发这个问题**，重要提交建议手工 `git push`。

---

## 五、写新脚本的规则

1. **先查本文件**，有现成的就别新写。
2. 新增脚本要在这里登记，并说明"什么时候用、和现有脚本什么关系"。
3. 需要断言就给**退出码**（0 通过 / 1 失败），别只打印。
4. 会改用户配置的脚本必须**自带备份/恢复**。
5. 参数化端口（3100 安装版 / 3101 开发实例），别写死。
6. 脚本要**幂等** —— Bash 工具可能重试执行同一条命令。
7. 含中文的 `.ps1` 存成 **UTF-8 with BOM**。
