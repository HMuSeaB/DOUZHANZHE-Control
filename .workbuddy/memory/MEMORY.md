# MEMORY.md — DOUZHANZHE-Control 项目长期记忆

## 构建 / 发布流程（关键约定，2026-08-19 验证）

- **一键打包脚本**：`installer/build-installer.ps1 -Version <稳定号>`（如 `2.0.1`）。它内部调用 `deploy.ps1`（前端构建+同步 wwwroot）、`.NET publish`、Inno Setup 编译，末尾 `[6.5]` 调 `sync-repos.ps1`。
- **版本号约定**：`package.json` 始终保留 **base 版本**（如 `2.0.1`），预发布后缀（`-pre.1`）只写在 **CHANGELOG 顶部标题 + git tag**。脚本 `[0/6]` 会识别 CHANGELOG 顶部为预发布（`含 -`）且传入稳定号时不覆盖它。
- **sync-repos.ps1 行为坑**：它**只在「工作树有未提交改动」时才提交**；对于已经 commit 但领先远程的提交，它**不会主动 push**。所以提交 changelog 后，必须手动：
  `git push origin feature/v2.0` 再 `git push origin v2.0.1-pre.1`（标签需单独 push，sync-repos 不推 tag）。
- **发布预发布版本 = 建 GitHub Release，不能只 push tag**：用户说"push到github"对预发布软件即指发到 **Releases**（带安装包附件）。仅 `git push --tags` 只会出现在 Tags 标签页、不进 Release 列表，等于没真正发布。正确做法：`gh release create vX.Y.Z-pre.N --title "斗战者控制台 vX.Y.Z-pre.N" --notes-file <changelog节> --prerelease <安装包.exe>`（gh 已登录 KanzakiK，token 含 repo 权限；release notes 文件要写到项目目录内，/tmp 会被沙箱回滚丢失）。
- **WorkBuddy 环境注意**：本会话曾因沙箱/安全删除防护多次卡构建。关闭沙箱、把 Node 批量删除阈值调到 9999、关闭"删除先移到回收站"保护后，整条 `build-installer.ps1` 才能跑通。前几次"卡死"实为工具防护，非脚本/代码问题。
- **v2.0.1-pre.1 changelog 内容范围**：仅含自 `v2.0.0-pre.1` 的真实增量（`cfg-` 配置模型迁移、powerPlan 高亮修复、ApplyCpuAsync、sync/import 接口、switch-stability-test.ps1、useControlState 重构+单测）。Toast/备份签名/应用内更新/平台控制接线等均不在此区间。
