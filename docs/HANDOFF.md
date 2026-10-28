# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-28 已实测 `47d7c4327fabd3c27b092e7f0fe6269ad119aa38` 与 `origin/dev` 一致，GitHub Actions run `40649388759` 成功；当前导入事务检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较，不能依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 旧模型导入事务与恢复测试边界审计，已完成本机验证，待提交同步检查点。
- **Last Completed：** Excel/GEDCOM 的旧成员整表替换与协作 JSON 树追加改为单一 SQLite 事务；空数据、重复/无效 ID、缺失父引用和导入内父子循环在写入前拒绝。写入失败时替换导入恢复旧表、追加导入不遗留部分成员。已将两组仍引用已删除集合名的恢复边界测试重新纳入 `ProcessWorkerExclusive`；生产恢复仍只存在测试原型，未接入。
- **Uncommitted Work：** `DataService`、旧模型导入调用点、协作导入、导入事务测试、恢复测试集合引用及本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。导入事务专项 5/5 通过；`Category!=Performance&Category!=UI` 为 119/119 通过；本轮构建成功（0 error）。未访问正式用户数据库。存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** GitHub Actions 尚未运行本导入事务修复；实际打印任务、真实磁盘/权限导致的发布失败、完整 Excel/GEDCOM 解析与版本化预检；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入现有格式仍缺少版本化预检和目标模型映射。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本旧模型导入事务检查点；继续保持生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认旧模型导入事务检查点已同步；保持生产恢复关闭并继续 P0。`
