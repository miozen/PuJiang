# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 经 GitHub Actions REST 核对 `402218d` 的 CI run `40981506626` 为 `success`。本轮开始时实际 `origin/dev` 和本地 HEAD 均为 `402218d`；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 HEAD 比较，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 目标关系/婚配的确认候选隔离与数据完整性；自动合并和生产恢复继续关闭。
- **Last Completed：** 已实现正式 ABA 代数 EF Migration、同一读取事务获取图和代数、条件删除原子检查代数和 schema、生产备份预检代数检查；旧库升级前保留快照，旧版备份依既有严格版本策略拒绝，新版备份预检通过。隔离测试覆盖跨进程姓名 ABA、记录/姓名 ABA、删除重插、代数/触发器损坏、读取一致性、显式降级再升级重装触发器及模拟 SQLite 人物表重建导致触发器丢失后的失败关闭。全库代数会使无关图写入也触发重新确认；无法防护有权限篡改代数或替换数据库。自动合并和生产恢复继续关闭。
- **Uncommitted Work：** 以 `git status --short` 为准；仅提交经验证的本轮代码、测试和文档。
- **Validation Performed：** 正式迁移/隔离集成测试 11/11 通过；日常 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore --no-build --filter "Category!=Performance&Category!=UI"` 217/217 通过；独立 UI 目标关系/婚配在途正常退出测试 1/1 通过；`dotnet-ef migrations has-pending-model-changes` 无模型差异。测试使用独立临时目录，未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，审计并验证 ABA 代数在批量导入、跨族谱无关写入、性能及正常退出竞争下的保守失效和事务边界，保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，审计并验证 ABA 代数在批量导入、跨族谱无关写入、性能及正常退出竞争下的保守失效和事务边界，保持自动合并和生产恢复关闭。`
