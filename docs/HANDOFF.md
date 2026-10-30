# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 已 fast-forward 至 `824ed65383188f75132389611d9999319ea3f94c`；GitHub 连接器未返回该 push 提交的 PR 工作流，公开 checks 页面不可读取且本机未安装 `gh`，故本轮 CI 尚不能确认，不得报为通过。后续接力仍须核对实际远端。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数草案的旧版共存、迁移外部写入、触发器重建与备份兼容接入验收；自动合并和生产恢复继续关闭。
- **Last Completed：** 新增两项待执行的测试专用隔离验收：验证旧模型写入在按谱安装事务持锁时等待、提交后由新触发器递增目标谱代数；验证 SQLite 重建 `People` 表丢失按谱触发器时读取和解除失败关闭。它们不修改生产 Migration、解除策略、自动合并或恢复。
- **Uncommitted Work：** `PuJiang.Tests/ScopedGenerationPrototypeTests.cs` 的两项按谱草案验收测试及本接力记录；另有既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除。
- **Validation Performed：** 当前源码的历史隔离验证为按谱批量导入定向 5/5、日常 269/269 通过。新增测试已通过 `dotnet build PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore`（0 error）；未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 新增两项隔离测试和日常测试须在可连接 `testhost` 的 Windows 宿主复跑；本机 `dotnet test` 在运行前 90 秒无法连接 `testhost` 后中止，不能计为通过。还须核对本轮提交后的远端 CI；草案的正式 Migration、WPF 集成、旧版共存及生产备份兼容尚未实现或验收；生产恢复、完整备份包、完整图读取性能及预检清理本身失败或进程终止仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在可连接 `testhost` 的稳定 Windows 宿主复跑新增按谱隔离测试与日常测试，核对远端 CI；通过后继续评审正式 Migration 的旧版共存与外部写入边界。生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，在隔离环境评估按谱草案的旧版共存、迁移期间外部写入及触发器重建边界，完善接入验收；保持生产全库代数、自动合并和生产恢复关闭。`
