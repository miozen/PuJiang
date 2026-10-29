# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 2026-10-29 已实测 `02b3dc0256c3430a360e7c533cd573ee704c6969` 与 `origin/dev` 一致，GitHub Actions run `40723794523` 成功；本轮 WPF 内部 JSON 导入入口检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 内部 JSON WPF 操作入口及隔离测试已完成，待提交同步检查点；默认导入继续失败关闭。
- **Last Completed：** 文件菜单新增独立“导入内部 JSON…”模态窗口，旧协作导入不受影响。文件选择、预检及提交使用 `TargetContextCoordinator` 外层许可；默认方式预选且冲突/无效预检会禁用确认，副本方式必须显式切换、重新预检、输入显示名称并二次确认。导入结果返回目标族谱 ID，主窗口据此刷新并选中新族谱；取消、拒绝与失败保持原选择。副本名称仅用于显示，不参与匹配。隔离 UI 子进程验证真实原生文件选择框取消时，正常退出会保留锁、拒绝新操作并等待操作结束；SQLite 专项继续覆盖默认冲突拒绝、副本重映射和写入失败完整回滚。未接入恢复。
- **Uncommitted Work：** 内部 JSON WPF 导入窗口、菜单接入、导入结果 ID/副本名称支持、测试和本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。Release build 成功（0 error）；内部 JSON 服务专项 9/9 通过；新增原生文件框取消/正常退出竞争 UI 测试 1/1 通过；`Category!=Performance&Category!=UI` 为 132/132 通过。未访问正式用户数据库，未启动生产恢复。存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** 本轮 GitHub Actions；完整 Excel/GEDCOM 解析与外部格式预检；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；内部 JSON 的正常 UI 交互尚未人工验收，外部格式仍未映射到目标模型。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本内部 JSON WPF 入口检查点；随后继续 P0 的外部格式版本化预检与目标模型映射，保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
