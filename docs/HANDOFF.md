# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-28 已实测 `3e72ff3dea1eb1fb00cc89046c900e13960e118c` 与 `origin/dev` 一致，GitHub Actions run `40652036300` 成功；本轮关系约束与事务检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较，不能依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 已固化内部 JSON 默认失败关闭与后续副本导入的业务契约；图数据批处理写入约束及隔离事务测试已完成，待提交同步检查点。
- **Last Completed：** 新增 D-012：内部 JSON 在冲突或不兼容时预检拒绝，不覆盖、删除或自动合并现有族谱；未来副本导入必须生成新族谱 ID 并重映射全部内部引用。新增目标模型图数据批处理入口，先校验同谱 ID、房支树、人物字段、关系和婚配，后在一个事务内写入；跨谱婚配、房支循环和真实数据库写入失败均不会留下部分数据。内部 JSON 仍只做内存映射，未接入 UI、持久化导入或生产恢复。
- **Uncommitted Work：** D-012、图数据事务写入/验证器及四项独立 SQLite 测试；以 `git status --short` 为准。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。图数据事务专项 4/4 通过；`Category!=Performance&Category!=UI` 为 126/126 通过；本轮 Release build 成功（0 error）。未访问正式用户数据库，未启动生产恢复。存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** 本轮 GitHub Actions；内部 JSON 持久化导入的冲突报告、默认拒绝写入和后续副本 ID 重映射；完整 Excel/GEDCOM 解析与外部格式预检；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；内部 JSON 尚无持久化导入或冲突报告，外部格式仍未映射到目标模型。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本图数据事务检查点；随后在 P0 实现内部 JSON 持久化导入的默认冲突拒绝报告，继续保持生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
