# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 经 GitHub Actions REST 核对 `0f32630` 的 CI run `40847085650` 和 `9895a70` 的 CI run `40848573952` 均为 `success`。本轮开始时实际 `origin/dev` 和本地 HEAD 均为 `9895a70`；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 HEAD 比较，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 目标关系/婚配的确认候选隔离与数据完整性；自动合并和生产恢复继续关闭。
- **Last Completed：** 候选绑定族谱、人物及选择版本；切换族谱或人物（包括切回）拒绝旧确认。外部已解除的关系或婚配由单条限定删除返回零行，报告过期并重读当前图；其他图记录保留。隔离 STA WPF 子进程构造确认控件验证外部解除及跨谱切换，隔离 SQLite 覆盖两种记录和人物往返选择。未显示模态窗口或做人工视觉验收。
- **Uncommitted Work：** 以 `git status --short` 为准；仅提交经验证的本轮代码、测试和文档。
- **Validation Performed：** 本轮定向 SQLite/WPF 测试 4/4 通过；`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore --filter "Category!=Performance&Category!=UI"` 165/165 通过。测试使用独立临时目录，未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 外部窗口的选择与确认后在途写入均已验收；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，审计并验证关系/婚配解除候选在确认期间记录被外部修改但 ID 不变时的陈旧内容保护和事务一致性，保持默认失败关闭、自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，审计关系/婚配解除确认期间记录内容被外部修改但 ID 不变时的陈旧内容保护与事务一致性，保持默认失败关闭、自动合并和生产恢复关闭。`
