# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 用户已确认 `377ac09`、`a6b4c8f`、`7ab4ceb`、`7f94250`、`982fcd2`、`42d13f4`、`fdb5521`、`3a14779`、`632f954`、`2a0e83e` 和 `3126a0c` 的 GitHub Actions 绿色。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 目标模型的关系/婚配新增及显式解除入口均已按 `GenealogyId` 约束并完成隔离 SQLite/WPF 验证；继续 P0 的数据完整性建设。D-013 已确认，自动合并和生产恢复继续关闭。
- **Last Completed：** 成员树工具栏已增加“解除关系/婚配”入口。它只读取当前人物参与的当前谱记录，选择改变会清除确认，必须选中一条记录并勾选确认后才可提交。持久化以同时限定族谱、记录和参与者的一条 SQL 删除完成；跨谱、陈旧/重复请求和底层删除错误都失败关闭，不删除人物、其它关系或另一类图记录。隔离 SQLite 覆盖成功、跨谱/陈旧拒绝及触发器失败，STA WPF 子进程验证确认按钮门槛及解除关系后保留婚配。未实施或模拟恢复，未触碰正式数据。默认失败关闭、自动合并和生产恢复继续关闭。
- **Uncommitted Work：** 无；以 `git status --short` 为准。
- **Validation Performed：** 用户已确认 `71a7206`、`48f33b9`、`97b4b5f`、`55a5a21`、`377ac09`、`a6b4c8f`、`7ab4ceb`、`7f94250`、`982fcd2`、`42d13f4`、`fdb5521`、`3a14779`、`632f954`、`2a0e83e` 和 `3126a0c` CI 绿色；目标解除 SQLite 定向测试 3/3、STA WPF 子进程测试 1/1、日常 `Category!=Performance&Category!=UI` 155/155 均通过。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 外部窗口的选择与确认后在途写入均已验收；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，审计并验证目标模型关系/婚配编辑与正常退出/排空协调的隔离竞争边界，继续保持默认失败关闭、自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
