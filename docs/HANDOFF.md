# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** `71a72068579917bb980a2904680a40869cd49cd5` 已推送至 `origin/dev`；GitHub Actions 结论尚待核实。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 外部格式显式失败关闭导入已接入窗口；补充并重跑该窗口的隔离退出竞争验证。D-013 已确认，自动合并和生产恢复继续关闭。
- **Last Completed：** 已实现 `ExternalGenealogyExchangeImportService`。Excel/GEDCOM 预检错误或缺少目标图零写入；有损映射必须显式 `acceptLosses`，否则零写入。成功导入始终创建新族谱并全量生成内部 ID，在同一事务保存图数据及 profile、版本、原始文件 SHA-256 指纹来源记录；不保存路径、不选择或修改既有族谱。D-013 已更新为 Accepted。
- **Uncommitted Work：** 外部格式损失报告窗口、菜单接入、退出竞争测试挂钩及本轮权威文档；以 `git status --short` 为准。
- **Validation Performed：** 用户已确认 `71a7206`/`48f33b9` CI 绿色；本轮 Windows 原生 Release build 成功（0 error，既有 ImageSharp 1.0.4 警告）；日常 `Category!=Performance&Category!=UI` 136/136 通过。新增 WPF 原生文件框退出竞争场景已编译，但整组 UI 理论测试受当前宿主输出时限中断，已终止该次隔离子进程，不能标记通过。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；外部格式窗口文件框至提交的隔离退出竞争测试；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 外部窗口已实现，但其隔离退出竞争未完成验收；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 提交并推送本轮 UI 检查点；随后在可完整等待子进程的环境重跑外部格式窗口隔离退出竞争测试，继续保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
