# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2 交互世系浏览；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 用户确认 D-024；已实施缓冲视口节点及连线呈现、滚动复用布局，并在本机开展隔离验证；结果和限制见 [视口测量](P2_VIEWPORT_2026-11-03.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Release 解决方案构建通过（0 错误，16 条警告）；日常非 UI/Performance 测试 349/349；显式隔离 Performance/UI 谱形 3/3、折叠基线 1/1，以及既有真实 WPF 窗口专项 1/1 通过；未访问正式用户数据库。详见 [视口测量](P2_VIEWPORT_2026-11-03.md)。
- **Validation Pending：** 本机非 D-024 指定参考机；参考机性能、三谱形完整交互边界矩阵及人工视觉均未验收。全展开通过测试工具预设；布局耗时未单独测量。当前改动尚无远端 CI。生产恢复与完整备份包未完成，候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在 D-024 指定参考机完成性能测量，并补齐三谱形交互边界与人工视觉验收；遵守 D-014，未完成前不宣称 D-024 或完整 P2 验收通过。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和最新 HANDOFF 恢复，在 D-024 指定参考机补齐性能、交互边界与人工视觉验收；遵守 D-014。`
