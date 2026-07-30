# DOUZHANZHE-Control — AGENTS.md

## 已否决 / 已废弃方案（AI 请勿重新引入）

### 1. Vite dev server / HMR 热更新
废弃原因：npm run build 仅 ~200ms，无需 dev server 的额外复杂度。
- vite.config.js 仅保留 build 配置，无 server.proxy
- 前端请求直连 C# API (localhost:3100)
- 构建后复制 dist/* → server/api/bin/run/wwwroot/
- 对应提交: abac20d, 49bbb69

### 2. Node.js 后端 (uxtu-backend)
废弃原因：所有端点已迁移到 C# API (Douzhanzhe.API)。
- 旧端口 3099 已彻底停用
- C# API 运行在 3100
- 对应提交: 61a48a1

### 3. localStorage 做配置持久化
否决原因：后端 API 是唯一权威源（single source of truth）。
- localStorage 仅做页面路由缓存 (dz_page)
- 主题等持久化配置全部走 POST /api/ui-state
- 对应决策: 架构讨论 (2026-07-29)

### 4. 消息桥 (WebView2 ↔ Shell)
否决原因：方案太重，用文件 IPC + FileSystemWatcher 替代。
- Shell 监听 server/config/ui-state.json
- API 原子写入 (File.WriteAllText → File.Move)
- 对应决策: 架构讨论 (2026-07-29)

### 5. WinRing0 / inpoutx64 / ryzenadj
废弃原因：已迁移到 pawnIO 驱动。
- 全部硬件控制通过 pawnIO.sys
- 对应分支: feature/pawnio-migration
- 对应提交: bae2314

## 架构要点（AI 请遵守）

- 前端所有 API 请求走相对路径 (/api/...)，由 C# API 处理
- 配置文件统一存放 server/config/（共享路径）
- Shell 标题栏主题同步：按钮 → API → 写入 server/config/ui-state.json → FileSystemWatcher → DWM
- 自动提交: .csproj AfterTargets=Build 执行 sync-repos.ps1
- 私有备份: KanzakiK/DOUZHANZHE-Control-private (docs/, canvas-workspace/)

## 开发环境限制

### apply_patch 工具（2026-07-30 已恢复）
切换至 CC-Switch 后，apply_patch 工具恢复正常。
CC-Switch 的协议转换层完整支持 Responses API 工具注册。
如需调整供应商配置，请通过 CC-Switch 管理工具操作，而非手动编辑 config.toml。
## 开发约定

- 新UI开发阶段严格按产品原型样式还原页面，不自行发挥
- API对接时发现原型缺内容，先找用户商量，不擅自补充
- 旧页面有意重写，不做增量修补

### 构建与部署
- 前端构建：
pm run build（〜200ms）
- 部署：运行 deploy.ps1（自动构建 + 复制 dist/* → server/api/bin/run/wwwroot/）
- 开发启动：运行 start-dev.ps1（自动检测端口占用、杀正式版、构建、启动 API）
- **禁止手动复制文件**，统一走脚本流程

### UI 迁移进度（2026-07-30）

#### 已完成
- 仪表盘（Dashboard）— 原型样式重写
- 控制面板（ControlPanel）— Intel CPU/Power/Performance + AMD CPU/Power
- 切换至 CC-Switch，apply_patch 工具恢复

#### 待完成
- 风扇控制（FanControl / FanCurvePanel）
- 平台控制（PlatformControl）
- 游戏（Games / GameProfilesPanel）
- 系统信息（SysInfo / SystemInfoPanel）
- 设置（Settings / SettingsPanel）