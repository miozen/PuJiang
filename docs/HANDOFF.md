# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 内部 JSON 持久化导入检查点已于 2026-10-28 推送至 `origin/dev`；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 内部 JSON 的默认失败关闭持久化导入及隔离事务验证已完成并已同步。
- **Last Completed：** `GenealogyExchangeImportService` 已将内部 v1 JSON 接入服务层持久化导入。它先返回可读预检报告：无效文档、已有来源族谱 ID、房支/人物/关系/婚配 ID 冲突均会分型拒绝且不写入；姓名、世代和排行不会被当作匹配依据，族谱名称也不再强制唯一。通过预检时，仅将来源 ID 不存在的完整图数据作为新族谱以单个 SQLite 事务写入；写入故障会回滚新族谱和全部图记录。未接入 WPF、未支持副本 ID 重映射、未持久记录导入来源/版本，也未接入生产恢复。
- **Uncommitted Work：** 仅本机既有 `ZupuPaiBan/App.xaml` 行尾差异；不属于本检查点，必须保持排除。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。内部 JSON 交换/导入与备份预检专项 15/15 通过（成功、同名不同 ID 导入、来源 ID 冲突拒绝、无效引用拒绝、数据库写入失败完整回滚，以及 Migration 集合校验）；`Category!=Performance&Category!=UI` 为 131/131 通过；Release 构建随测试成功（0 error）。未访问正式用户数据库，未启动生产恢复。NuGet 漏洞索引不可访问；存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** 本轮 GitHub Actions；内部 JSON 的副本 ID 重映射、导入来源/版本追溯与 WPF 操作入口；完整 Excel/GEDCOM 解析与外部格式预检；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；内部 JSON 尚无副本导入、来源/版本持久追溯或 WPF 入口，外部格式仍未映射到目标模型。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在 P0 设计并实现导入来源/版本追溯的持久化边界，继续保持生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
