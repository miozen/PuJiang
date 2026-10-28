# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-28 本轮修改前已 `git fetch origin`，本机 `941b1a52b7295dcda377aade76d83b02492edecf` 与 `origin/dev` 一致；当前检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较，不能依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** XPS 发布失败清理验证及 CI 跨进程测试稳定性，已完成本机验证，待提交同步检查点。
- **Last Completed：** XPS 在真实 `XpsDocument` 临时写入完成后注入发布失败，验证所选目标及同目录临时文件均被清理、失败状态更新，以及保存框期间正常退出的排空、锁保持与新操作拒绝；不调用打印队列。PDF 同时补足临时文件残留断言。将数据目录锁、恢复原型/SQLite 句柄、恢复中断和 WPF 子进程测试纳入同一非并行 xUnit 集合，针对 GitHub Windows runner 上的子进程 READY/退出超时。生产恢复未接入。
- **Uncommitted Work：** `PuJiang.Tests/ProcessWorkerExclusiveCollection.cs`、相关跨进程测试、`PuJiang.Tests/WpfAppExitWorker.cs`、`ZupuPaiBan/Services/PrintService.cs` 及本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。XPS 发布失败与退出专项 1/1 通过；与 GitHub Actions 相同的 `Category!=Performance&Category!=UI` 筛选为 114/114 通过。未访问正式用户数据库，未提交任何打印任务。存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** GitHub Actions 尚未运行本修复；实际打印任务、真实磁盘/权限导致的发布失败、导入文件实际解析/提交和历史 UI 间歇超时；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本 XPS 发布失败清理与 CI 稳定性检查点；继续保持生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认 XPS 发布失败清理及 CI 跨进程稳定性检查点已同步；保持生产恢复关闭并继续 P0。`
