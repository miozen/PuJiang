# 接力状态

- **Last Updated：** 2026-11-02（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 本轮开始本地为 `f61bc91`，提交前已 fetch；后续以实际远端核对为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P1 完整族谱管理；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 按 D-017 补全未详婚配的同谱另一方，保留记录身份和资料并按无向规则排序；事务核对旧快照、人员、重复和全库代数，隔离 SQLite 与 WPF 窗口验收覆盖取消、确认、重载及失败路径。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows Release 解决方案单进程构建通过（0 错误，24 条既有或环境告警）；日常非 UI/Performance 回归 334/334、隔离 SQLite 专项 2/2、实际 WPF 窗口专项 1/1 通过；未访问正式用户数据库。NuGet 审计源不可达，ImageSharp 已知告警仍在。
- **Validation Pending：** `1072948` 的 GitHub Actions 构建通过，日常测试 330/332：两个恢复原型子进程测试分别等待进程退出及就绪超时；原因未确定，已重跑失败作业。本轮远端 CI 状态待确认。本机未执行人工视觉验收和 Performance 专项，生产恢复与完整备份包未完成；候选切换和相关深层专项暂停。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 跟踪上一远端 CI 重跑及本轮 CI，并评估 P1 后续关系维护用例，遵守 D-014。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，跟踪远端 CI 并评估 P1 后续关系维护用例；遵守 D-014。`
