# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P2 交互世系浏览；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 用户确认 D-023；目标模型新增与树形共享主线会话和图表配置的只读节点连线图，支持展开、定位、路径高亮、缩放和滚动。千人图形原始测量及边界见 [图形基线](P2_GRAPH_BASELINE_2026-11-03.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Release 解决方案构建通过（0 错误，31 条警告）；日常非 UI/Performance 测试 343/343；隔离投影专项 1/1、真实 WPF 窗口专项 1/1、千人图形测量 1/1 均通过，原始数据见 [图形基线](P2_GRAPH_BASELINE_2026-11-03.md)。未访问正式用户数据库。
- **Validation Pending：** 本轮检查点远端 CI 待核实；图形无已确认性能阈值，深链、宽分支、全展开、其他设备、人工视觉与完整 P2 未验收。当前对所有可见节点创建控件，并非视口虚拟化。生产恢复与完整备份包未完成，候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 结合千人图形基线评估性能验收目标及深链、宽分支、全展开和按需渲染范围，提出推荐方案与可操作验收；遵守 D-014，基线不等于性能通过或完整 P2 验收。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按最新 HANDOFF 结合 P2 图形基线，评估性能验收目标及深链、宽分支、全展开与按需渲染范围，提出推荐方案，遵守 D-014。`
