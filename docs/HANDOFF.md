# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-28 已实测 `a30a735cd08968de9acc855ea16b874621e578e1` 与 `origin/dev` 一致。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 显式内部 JSON 副本导入及全量 ID 重映射已实现并验证，待提交同步检查点；默认导入继续失败关闭。
- **Last Completed：** `ImportCopyAsync` 是与默认导入分离的显式副本用例：允许来源族谱 ID 已存在，先完成结构/格式/引用预检，再为目标族谱、人物、房支、关系和婚配生成全新 ID，重写所有内部引用，并在一个 SQLite 事务内写入图数据与 `Copy` 模式来源记录。它不按名称、世代或排行匹配，不自动合并，不复制附件，也不接入恢复流程。
- **Uncommitted Work：** 副本导入实现、Migration、隔离 SQLite 测试及对应契约文档；另有本机既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除在提交之外。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。内部 JSON 导入专项 9/9 通过（默认与副本导入、来源记录、嵌套房支/人物/关系/婚配 ID 重映射、冲突拒绝和写入失败回滚）；EF 工具报告无待生成 Migration；`Category!=Performance&Category!=UI` 为 132/132 通过。未访问正式用户数据库，未启动生产恢复。NuGet 漏洞索引不可访问；存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** 本轮 GitHub Actions；内部 JSON 导入的 WPF 操作入口；完整 Excel/GEDCOM 解析与外部格式预检；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；内部 JSON 尚无 WPF 入口，外部格式仍未映射到目标模型。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本显式副本导入检查点；随后在 P0 设计内部 JSON 导入的 WPF 操作入口，继续保持默认导入失败关闭、自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
