# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2-1 横向纯布局最小切片；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 确认 D-026 主入口优先于 D-011，保留历史及旧入口；记录 [D-027](DECISIONS.md#d-027--世系谱书资料与出版边界)。P2-1 纯布局已复核多根、异尺寸、不同展开状态、节点与不相关边避让及数值极限；修复极小宽度因浮点舍入而消失却仍返回有效结果的问题，并拒绝不能表示转折点的连线。未接 WPF 或数据库。先前图表复核可读性任务仍保留。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows Release 解决方案构建成功，纯布局定向 21 项、日常回归 370 项通过，均无失败或跳过。`--no-restore` 复用现有还原，`--no-build` 在相同源码和 Release 解决方案构建后使用。新测试先复现极小宽度缺陷，修复后通过。未访问正式用户数据库。
- **Validation Pending：** 核对包含本轮代码的提交及其远端 CI；阶段验收不能以本地结果代替远端 CI。D-025 世代、排序、复杂配偶等规则尚待确认；目标工作台、草稿、横向画布及按需查询均未实现。其他设备和完整 P2 尚未验收；D-014 关闭项不变。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对包含本轮 P2-1 实现和验证的提交及其远端 CI，依据实际结果判断受限切片是否验收；不推断未确认业务规则，遵守 D-014。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025/D-026 恢复，核对包含 P2-1 布局及边界测试的提交和远端 CI，依据实际结果判断受限切片验收；遵守 D-014。`
