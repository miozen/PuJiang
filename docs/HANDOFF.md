# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P2 交互世系浏览；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 用户确认 D-022；树形浏览支持 SQLite 按图表显式保存主线、多图表和失效引用复核，迁移与备份边界见 [决策](DECISIONS.md#d-022--按图表保存主线配置)和[架构](ARCHITECTURE.md)。既有 D-020 参考机测量见 [P2 树形测量](P2_TREE_ACCEPTANCE_2026-11-03.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Release 解决方案构建通过（0 错误，31 条警告）；日常非 UI/Performance 测试 343/343；隔离 SQLite 图表专项 2/2，真实 WPF 窗口专项 1/1（保存、重开、失效项阻止保存与清除、复核窗口）通过；EF 模型与迁移快照一致。隔离测试包含旧库升级、旧包拒绝、新包预检及读取包内图表数据。一次并行构建因测试进程占用 DLL 失败，等待结束后重建通过。未访问正式用户数据库。
- **Validation Pending：** 本轮检查点远端 CI 待核实；其他设备及谱形性能、人工视觉验收和完整 P2 图形未完成；配置加载性能未另测。生产恢复与完整备份包未完成，候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 评估 P2 完整图形世系视图的范围、交互、现有投影复用及可操作验收，向用户提出推荐方案；遵守 D-014，树形浏览和参考场景性能不等于 P2 完整验收。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和最新 HANDOFF 恢复，评估 P2 完整图形世系视图范围、交互和验收，提出推荐方案，遵守 D-014。`
