# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-29 已实测 `f4e761b225135b04686d628a8ac425a6bae4bafb` 与 `origin/dev` 一致，GitHub Actions run `40793486623` 成功；本轮外部格式导入设计文档尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 外部格式的显式失败关闭持久化导入与损失报告 UI 已形成 Proposed 设计，等待 D-013 的两项用户确认；保持失败关闭。
- **Last Completed：** D-013 Proposed 固化：外部格式只导入为新族谱，预检错误零写入；损失报告按分类显示，拟要求用户独立确认后才提交。入口与旧成员整表替换分离，文件选择至提交受协调器许可保护，成功后刷新并选中新族谱。未实现数据库写入、损失确认 UI 或外部来源标识。
- **Uncommitted Work：** D-013 Proposed、外部导入 UI/事务设计和本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** 设计轮未改动可执行代码；沿用 `f4e761b` 已通过的 Windows 原生验证：Release build 0 error、外部 profile 3/3、日常测试 135/135。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 用户确认 D-013 的“损失确认后是否允许导入”及外部来源稳定标识；确认后实施外部持久化导入、损失报告 UI、事务/退出竞争测试；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 外部 profile 仍仅为内存映射；不能在 D-013 确认前把有损映射写入数据库。独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 等待用户确认 D-013 的两个待确认点；确认后实施外部格式持久化导入与损失报告 UI，继续保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
