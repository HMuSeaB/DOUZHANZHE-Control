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
