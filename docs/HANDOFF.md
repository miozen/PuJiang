# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-29 已实测 `310f509a274927b6ec5ae88fac644fd7ffa3bf55` 与 `origin/dev` 一致；本轮外部格式预检检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** ExcelLegacyV1 与 GEDCOM-5.5.x 的只读预检和目标模型映射已完成，待提交同步检查点；保持失败关闭。
- **Last Completed：** 新增 `ExternalGenealogyExchangePreflightService`：ExcelLegacyV1 要求九列固定表头、按数据行序号解析父亲引用并报告不能安全匹配的配偶姓名文本；GEDCOM-5.5.x 映射人物、家庭父母/子女及双亲婚配，未支持标签进入损失报告。两种 profile 对版本、结构、姓名、性别和引用失败关闭，只返回内存目标图，不写数据库、不替换旧成员、不按名称/世代/排行匹配，也不接入恢复。
- **Uncommitted Work：** 外部格式预检服务、三个独立文件 profile 测试及本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。Release build 成功（0 error）；外部 profile 专项 3/3 通过；`Category!=Performance&Category!=UI` 为 135/135 通过。未访问正式用户数据库，未启动生产恢复。存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** `310f509` 与本轮 GitHub Actions；外部 profile 的持久化导入/损失报告 UI；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；外部 profile 尚无持久化导入或 UI，且仅覆盖明确列出的兼容子集。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本外部 profile 预检检查点；随后在 P0 设计外部格式的显式、失败关闭持久化导入与损失报告 UI，继续保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
