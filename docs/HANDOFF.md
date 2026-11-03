# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P2 交互世系浏览；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 用户确认 D-020/D-021；树形浏览支持会话内已选上系路径高亮、边界提示和清除选择。参考机 D-020 专项已测量，原始数据及适用边界见 [P2 树形测量](P2_TREE_ACCEPTANCE_2026-11-03.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 复用有效还原的 Release 解决方案构建通过（0 错误，23 条告警）；日常非 UI/Performance 回归 341/341；隔离 SQLite 与真实 WPF 窗口定向测试 2/2 通过；显式 Performance/UI 专项 1/1 通过，参考场景 D-020 限值均满足，原始数据见 [P2 树形测量](P2_TREE_ACCEPTANCE_2026-11-03.md)。首次 WPF 定向测试误预期折叠后选中子节点不变而失败；已按 WPF 自动选中可见父节点的实际行为核对不改变选线并重跑通过。未访问正式用户数据库。此前两个恢复原型子进程超时的根因未确定。
- **Validation Pending：** 本轮检查点远端 CI 待核实；其他设备及谱形性能、人工视觉验收、完整 P2 图形和按图表配置未完成；生产恢复与完整备份包未完成，候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 评估并提出按图表主线配置的稳定 ID、保存方式、迁移/备份和失效引用处理方案及验收；先向用户说明推荐与替代方案，不擅自改变原始关系。遵守 D-014，当前树形浏览和参考场景性能不等于 P2 完整验收。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，评估 P2 按图表主线配置持久化方案及验收，提出推荐方案，遵守 D-014。`
