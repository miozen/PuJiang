# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 经 GitHub Actions REST 核对 `24000ec` 的 CI run `40976854837` 为 `success`。本轮开始时实际 `origin/dev` 和本地 HEAD 均为 `24000ec`；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 HEAD 比较，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 目标关系/婚配的确认候选隔离与数据完整性；自动合并和生产恢复继续关闭。
- **Last Completed：** 关系/婚配候选保留完整记录快照；单条 SQL 删除比较所有记录字段和族谱、参与者，外部修改同 ID 记录后零行拒绝并刷新当前图。隔离 SQLite 在删除执行前由另一上下文修改 12 个字段，逐项验证记录及其他图数据保留；另验证备注非空变空后重新读取再确认。隔离 STA WPF 构造确认控件覆盖婚配备注修改；未显示模态窗口或做人工视觉验收。字段改变后恢复原值的 ABA、关联人物名称变化尚未覆盖。
- **Uncommitted Work：** 以 `git status --short` 为准；仅提交经验证的本轮代码、测试和文档。
- **Validation Performed：** 本轮定向 SQLite/WPF 测试 16/16 通过；`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore --filter "Category!=Performance&Category!=UI"` 179/179 通过。测试使用独立临时目录，未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 外部窗口的选择与确认后在途写入均已验收；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，审计关系/婚配确认期间候选所显示的人物姓名被外部修改的陈旧展示及事务边界，保持默认失败关闭、自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，审计关系/婚配解除确认期间候选所显示的人物姓名被外部修改时的陈旧展示和事务边界，保持默认失败关闭、自动合并和生产恢复关闭。`
