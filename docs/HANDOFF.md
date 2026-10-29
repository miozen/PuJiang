# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 用户已确认 `377ac09` 的 GitHub Actions 绿色；本轮源文件版本复核检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 外部格式窗口的选择与提交在途退出竞争均已验证；继续 P0 的数据完整性建设。D-013 已确认，自动合并和生产恢复继续关闭。
- **Last Completed：** 已实现 `ExternalGenealogyExchangeImportService`。Excel/GEDCOM 预检错误或缺少目标图零写入；有损映射必须显式 `acceptLosses`，否则零写入。成功导入始终创建新族谱并全量生成内部 ID，在同一事务保存图数据及 profile、版本、原始文件 SHA-256 指纹来源记录；不保存路径、不选择或修改既有族谱。预检成功后会复核源文件 SHA-256；复核不一致或源文件不可读时返回 `SourceChangedAfterPreflight`，不进入写入事务。D-013 继续保持 Accepted。
- **Uncommitted Work：** 源文件版本复核实现、Excel/GEDCOM 零残留隔离测试及本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** 用户已确认 `71a7206`、`48f33b9`、`97b4b5f`、`55a5a21` 和 `377ac09` CI 绿色；Windows 原生 Release build 成功（0 error，既有 ImageSharp 1.0.4 警告）；外部预检测试 7/7 通过；日常 `Category!=Performance&Category!=UI` 139/139 通过；GEDCOM 替换和 Excel 篡改均被拒绝且图数据和来源记录零残留。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 外部窗口的选择与确认后在途写入均已验收；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 提交并推送本轮源文件版本复核检查点，确认远端 CI；随后继续 P0 的关系约束、事务和隔离测试建设，继续保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
