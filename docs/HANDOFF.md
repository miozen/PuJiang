# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；本文件由 P0 输出退出协调检查点提交。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 推送前核实远端 `dev` 为 `6e7d183`；是否包含本检查点须以 `git ls-remote origin refs/heads/dev` 和本机 `HEAD` 比较确认，不能依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 预览 PDF/XPS 与打印对话框的退出协调验证。
- **Last Completed：** PDF、XPS、打印命令登记输出操作许可；隔离进程验证 PDF 取消及确认、XPS 取消与正常退出竞争；打印专项未通过，范围及 XPS 缺陷见 [架构](ARCHITECTURE.md)。生产恢复未接入。
- **Uncommitted Work：** 检查点提交时不包含本机 `ZupuPaiBan/App.xaml` 既有行尾差异；其他未提交工作以 `git status --short` 核实。
- **Validation Performed：** Windows SDK 10.0.402；Release 构建成功；PDF/XPS 专项 3/3、UI 回归 29 通过、1 跳过，日常测试（`Category!=Performance&Category!=UI`）114/114 通过；仅使用独立临时数据，PDF 实际保存到指定测试路径。打印专项两次超时，调用后未识别到可操作的测试进程打印窗口；本机可列出三个打印队列，不足以确定根因，专项保留为跳过。未确认 XPS 保存或提交打印任务。历史 UI 间歇超时仍未完全定位；NuGet 漏洞索引不可访问。
- **Validation Pending：** 打印对话框及实际打印的退出竞争；XPS 输出修复和确认保存；PDF/XPS 故障中途、其他文件对话框、历史 UI 间歇超时；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** XPS 导出未使用所选路径且调用打印队列，可能触发非预期打印；打印隔离测试超时原因待查。独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 暂停开发并等待用户确认；获确认后修复 XPS 导出以安全写入用户选择的文件而不调用打印队列，在隔离临时目录验证成功、失败和退出竞争；继续保持生产恢复关闭。
- **Cross-machine Ready：** NO（本机存在未提交的 `ZupuPaiBan/App.xaml` 行尾差异，不属于本检查点）；另一电脑可在核对远端包含检查点后按下述步骤恢复已提交工作，不会获得此本机差异。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，报告状态；暂不开始 XPS 修复，等待我确认。`
