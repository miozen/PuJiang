# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2 横向世系图设计；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 按用户明确的十项要求记录 [D-025](DECISIONS.md#d-025--横向世系图的参考布局定义)，审查现有能力并形成 [独立布局、WPF 和验收草案](HORIZONTAL_LINEAGE_DESIGN.md)；本轮未实施。先前建议的图表复核可读性仍待处理，见 [计划](CURRENT_PLAN.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 本轮核对相关源码、测试和契约，并只读核实远端提交；文档和差异检查结果以本轮实际汇报为准。此前 Windows Release 构建通过（0 错误、16 警告），日常非 UI/Performance 349/349，三谱形扩展专项 3/3；对应功能与测试代码的远端 CI 通过。未访问正式用户数据库。
- **Validation Pending：** 本轮无代码修改，未运行构建、测试或人工验收；未看到用户提及的参考截图，仅按其文字定义设计。横向图、按需数据查询、复杂关系规则与验收仍未实现或确认；原图表复核可读性问题仍在。其他设备和完整 P2 尚未验收；D-014 关闭项不变。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 围绕 D-025 的首个横向图切片，用固定谱例和反例细化世代对齐、排行、配偶、不可避免交叉及按需读取的一致性规则与验收；需业务决定时先呈现具体方案并取得用户确认，然后实施独立布局引擎与 WPF 视图，遵守 D-014。原图表失效复核可读性任务保留，不在本轮实施。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025 恢复；细化横向世系图首个可交付切片及待确认规则，取得必要决策后实施与验收，遵守 D-014。`
