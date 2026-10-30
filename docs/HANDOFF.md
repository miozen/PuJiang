# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 经 GitHub Actions REST 核对 `60a04bb` 的 CI run `40980301489` 为 `success`。本轮开始时实际 `origin/dev` 和本地 HEAD 均为 `60a04bb`；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 HEAD 比较，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 目标关系/婚配的确认候选隔离与数据完整性；自动合并和生产恢复继续关闭。
- **Last Completed：** 在隔离旧结构库验证测试专用 ABA 迁移设计：迁移前快照可读、事务安装失败回滚，代数缺失/损坏/溢出或触发器缺失/同名替换时读取或条件删除失败关闭；真实写锁竞争、外部 ABA 和删除重插均保留旧候选不得删除。旧版生产备份预检拒绝原型结构；测试专用备份检查可接受有效原型包并拒绝代数/触发器损坏，活动库和包不变。该检查尚未组合完整生产预检；没有新增正式 EF Migration 或修改生产解除逻辑，ABA 缺口仍存在。方案与边界见 [架构](ARCHITECTURE.md)。
- **Uncommitted Work：** 以 `git status --short` 为准；仅提交经验证的本轮代码、测试和文档。
- **Validation Performed：** 本轮测试专用迁移/备份定向测试 14/14 通过；`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore --no-build --filter "Category!=Performance&Category!=UI"` 206/206 通过。测试使用独立临时目录，未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** ABA 旧确认仍可解除记录，当前测试原型不是修复；独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，评估并实现正式 EF Migration 与 ABA 防护接入，补齐同一读事务、真实备份预检、新旧库升级、跨进程及触发器重建验证；先解决原型已知限制，不启用生产恢复或自动合并。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，基于隔离原型评估并实现正式 EF Migration 和 ABA 解除保护，验证同一读事务、新旧备份预检、旧库升级、触发器重建和跨进程；保持自动合并和生产恢复关闭。`
