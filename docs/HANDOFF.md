# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；当前检查点为 `fix(p0): export XPS to selected file`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-28 已将模态协调检查点 `5daa04a5c75d9e2c6aee75b7a0e8ca0bc89f9b0a` 推送至 `origin/dev` 并实测远端包含该提交；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较，不能依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 照片与旧式导入模态入口的退出协调验证，已完成并推送检查点。
- **Last Completed：** 照片选择、Excel/GEDCOM 导入文件选择、协作导入文件选择及确认框均在显示期间登记协调器许可；隔离进程在五个真实模态框出现时请求正常退出，验证排空等待、锁保持和新操作拒绝，取消文件选择或在协作确认框选择“否”。首次审计发现协作选文件框未登记会导致 WPF 关闭重入异常，现已修复。生产恢复未接入。
- **Uncommitted Work：** 模态协调检查点包含 `ZupuPaiBan/MainWindow.xaml.cs`、相关 ViewModel/协作导入对话框、隔离 UI 测试及计划、架构、接力文档；提交后应为空，以 `git status --short` 为准。
- **Validation Performed：** Windows 原生 .NET 测试使用独立 `artifacts/modal-*` 输出目录。照片、Excel、GEDCOM、协作选择、协作确认五项隔离 UI 专项逐项通过；日常测试 `Category!=Performance&Category!=UI` 为 114/114 通过。未访问正式用户数据库。存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** 打印对话框及实际打印的退出竞争；真实文件系统中途失败、PDF 故障中途、导入文件实际解析/提交和历史 UI 间歇超时；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 打印隔离测试超时原因待查。独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对远端 `dev` 已包含当前检查点和工作区干净后，等待用户指定下一项 P0 工作；继续保持生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认照片及导入模态退出协调检查点已同步；保持生产恢复关闭并等待下一项 P0 指令。`
