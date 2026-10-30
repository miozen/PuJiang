# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** `1de2e85bb120ecbe2786f69504e833e0792d7591` 已推送至 `origin/dev`；其 GitHub Windows CI 为 271/272，唯一失败显示 `BeginTransactionAsync` 也会在取得 SQLite 写锁前同步等待，导致原 DDL 前同步点永远不可达。当前工作树已将同步点移到开启事务前；后续接力须用实际远端确认本次修复。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数草案的正式 Migration、旧版共存、迁移外部写入、触发器重建与备份兼容接入验收；自动合并和生产恢复继续关闭。
- **Last Completed：** `1de2e85` 的 Windows CI 日常测试为 271/272，进一步确认 SQLite 的锁等待发生在 `BeginTransactionAsync`；当前修复将独立线程同步点前移到开启事务前，使既有写锁释放可被确定控制，保留“外写先提交为基线”的验收语义。按谱正式 Migration 的双轨代数、旧版写入、触发器重建和严格备份拒绝已写成可执行清单；不修改生产 Migration、解除策略、自动合并或恢复。
- **Uncommitted Work：** 仅既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除。
- **Validation Performed：** 当前修复后本机 `dotnet build PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore` 为 0 error。`1de2e85` 的 GitHub Windows CI 为 271 passed / 1 failed；失败详情已用于本轮修复。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 同步点前移后的“外部写入先持锁”用例待本轮代码提交触发的 Windows CI 验证；本机 vstest 仍在运行前 90 秒无法连接 `testhost`，不重复等待。正式 Migration、WPF 集成、旧版共存及生产备份兼容尚未实现或验收；生产恢复、完整备份包、完整图读取性能及预检清理本身失败或进程终止仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 推送本轮修复后，仅在下一个代码检查点或用户请求时核对 Windows CI；继续 P0，在隔离 SQLite 原型中将正式 Migration 清单逐项转为旧版启动/写入、触发器重建和双向备份拒绝测试。生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，在隔离环境评估按谱草案的旧版共存、迁移期间外部写入及触发器重建边界，完善接入验收；保持生产全库代数、自动合并和生产恢复关闭。`
