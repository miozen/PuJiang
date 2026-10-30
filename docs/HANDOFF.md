# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** `458fe8dbe41fc5ddaa5cc46815ebacd0f5eef161` 已推送至 `origin/dev`；用户已确认其前一 `5f64198` Windows CI 为 green。当前工作树包含下一轮旧模型启动/安装中断验收测试，后续接力须用实际远端确认该新检查点。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数草案的正式 Migration、旧版共存、迁移外部写入、触发器重建与备份兼容接入验收；自动合并和生产恢复继续关闭。
- **Last Completed：** 新增待验证的隔离原型测试：旧模型已打开连接并读取后，按谱安装提交仍可由旧写入路径更新，数据库触发器递增对应谱代数；安装注入失败回滚后没有按谱表或触发器残留，旧写入路径仍可更新数据。它们不修改生产 Migration、解除策略、自动合并或恢复。
- **Uncommitted Work：** 仅既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除。
- **Validation Performed：** 用户确认 `5f64198` 的 GitHub Windows CI 已绿；本轮新增测试已通过本机 `dotnet build PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore`（0 error）。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮旧模型启动/安装中断用例及前一轮备份双向拒绝、触发器重装用例待本轮代码提交触发的 Windows CI 验证；本机 vstest 仍在运行前 90 秒无法连接 `testhost`，不重复等待。正式 Migration、WPF 集成、旧版共存及生产备份兼容尚未实现或验收；生产恢复、完整备份包、完整图读取性能及预检清理本身失败或进程终止仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 推送本轮旧模型启动/安装中断验收后，仅在下一个代码检查点或用户请求时核对 Windows CI；继续 P0，评审原型验收是否已足以进入正式 Migration 实现前的设计评审。生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，在隔离环境评估按谱草案的旧版共存、迁移期间外部写入及触发器重建边界，完善接入验收；保持生产全库代数、自动合并和生产恢复关闭。`
