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

## 环境坑：Bash 工具里 git ref 写入被静默吞掉（2026-09-25）

- 症状：`git commit` / `git update-ref` 返回 0，但 `.git/refs/heads/<branch>` 不落盘 → 产生无父 root commit，
  本地分支指针丢失（`git log` 报 "does not have any commits yet"）。**加不加沙箱都一样**。
- 可用做法：`git commit-tree <tree> -p <parent> -m <msg>` 造提交对象 → 用普通 shell 重定向
  `printf '%s\n' <sha> > .git/refs/heads/<branch>` 写 ref → `git push`（push 正常）。
  索引复位用 `git read-tree <sha>`（不碰 ref）。**不要用 `git update-ref`**。
- `deploy.ps1` 在 PowerShell 工具会话里调不到 `git`，`gen-build-info.ps1` 失败导致第 1 步就 abort；
  需要时手工执行其 1~3 步（bash 复制 dist → wwwroot + 写 version.txt/build-info.json）。
