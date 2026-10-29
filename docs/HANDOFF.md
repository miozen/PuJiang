# 接力状态

- **Last Updated：** 2026-10-29（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 用户已确认 `377ac09`、`a6b4c8f`、`7ab4ceb`、`7f94250`、`982fcd2`、`42d13f4`、`fdb5521`、`3a14779`、`632f954`、`2a0e83e`、`3126a0c` 和 `e5fe501` 的 GitHub Actions 绿色；经 GitHub Actions REST 核对 `7ec13a5` 的 `CI` run `40813492087`、`098b932` 的 `CI` run `40814373596`、`e710b31` 的 `CI` run `40839784285`、`6fc55cd` 的 `CI` run `40841610662` 及 `7dc161f` 的 `CI` run `40845257804` 均已 `success`。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 目标模型的档案、关系/婚配及成员树上下文生物父子入口均按 `GenealogyId` 约束，并已验证写入在途切换族谱、忙锁重试耗尽、写入失败及写后刷新失败时的事务作用域、状态提示和视图隔离；继续 P0 的数据完整性建设。D-013 已确认，自动合并和生产恢复继续关闭。
- **Last Completed：** 隔离 SQLite 在目标人物关系的 `INSERT`、婚配的 `INSERT`、关系的 `DELETE` 和婚配的 `DELETE` 已成功后注入后续刷新读取失败。每种图写入仍完整提交，成员树清空而不展示陈旧状态，并准确报告“人物关系/婚配关系已新增/已解除，但刷新失败”；成功文案不会覆盖失败状态。解除故障后重新选择可加载实际图数据。全部数据库均为临时测试文件；未实施、模拟或启动恢复，未触碰正式数据。默认失败关闭、自动合并和生产恢复继续关闭。
- **Uncommitted Work：** 本轮为目标关系/婚配写后刷新状态保护、隔离提交可见性测试及文档；提交前以 `git status --short` 为准。
- **Validation Performed：** 用户已确认 `71a7206`、`48f33b9`、`97b4b5f`、`55a5a21`、`377ac09`、`a6b4c8f`、`7ab4ceb`、`7f94250`、`982fcd2`、`42d13f4`、`fdb5521`、`3a14779`、`632f954`、`2a0e83e`、`3126a0c` 和 `e5fe501` CI 绿色；GitHub Actions REST 已确认 `7ec13a5` 的 `CI` run `40813492087`、`098b932` 的 `CI` run `40814373596`、`e710b31` 的 `CI` run `40839784285`、`6fc55cd` 的 `CI` run `40841610662` 及 `7dc161f` 的 `CI` run `40845257804` 成功。本轮定向 SQLite 测试 3/3、日常 `Category!=Performance&Category!=UI` 161/161 均通过。日常结果由隔离 TRX 读取确认；未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 外部窗口的选择与确认后在途写入均已验收；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，审计并验证目标模型关系/婚配解除候选读取在 SQLite 忙锁、跨族谱切换或确认间陈旧选择下的失败关闭、候选隔离与事务一致性，继续保持默认失败关闭、自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，并确认本轮 CI；继续 P0 审计目标模型关系/婚配解除候选读取在 SQLite 忙锁、跨族谱切换或确认间陈旧选择下的失败关闭、候选隔离与事务一致性，保持默认失败关闭、自动合并和生产恢复关闭。`
