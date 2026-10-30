# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** V1 实现 `5070ab3aaf980015a87cf408a14ed4e98f1ffab5` 及快照报告修复 `caceba9` 已推送至 `origin/dev`；用户已确认其前一 `5f64198` Windows CI 为 green。本机未安装 `gh`，可用 GitHub 连接器也未返回该 push workflow 的状态，故本轮 CI 仍待实际外部核对。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数 V1 Migration 已实现后的接入验收与下一步边界评审；自动合并和生产恢复继续关闭。
- **Last Completed：** 已实施 `20261030000200_AddScopedUnlinkGenerationV1` 和 `ScopedUnlinkGenerationV1`：两张无外键活动/墓碑表、11 个不可变触发器、事务化升级/降级、15 秒 `BUSY`/`LOCKED` 失败报告、正式备份预检和真实 `People` 重建后的双轨触发器重装均有隔离验收；锁发生在快照前时不会报告已被清理的快照路径。生产解除候选仍为全库代数。详见[V1 实现设计](SCOPED_UNLINK_GENERATION_V1_DESIGN.md)。
- **Uncommitted Work：** 仅既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除。
- **Validation Performed：** 本机 `dotnet build PuJiang.slnx --configuration Release --no-restore` 通过（0 error）；日常非 UI/Performance 测试 277/277 通过；V1 专项隔离 SQLite 4/4 通过；`dotnet-ef migrations has-pending-model-changes --no-build` 报告无模型差异。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮 V1 实现提交后的 Windows CI；旧程序二进制对新 V1 包的独立预检演练、按谱候选读取/解除切换及其 WPF/退出验收均未实施。生产恢复、完整备份包、完整图读取性能及预检清理本身失败或进程终止仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在可用 GitHub 状态入口核对 `5070ab3` 的 Windows CI；随后继续 P0，独立评审按谱候选读取/解除是否具备接入条件，先补旧程序预检演练与候选/WPF/退出隔离验收。生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，在隔离环境评估按谱草案的旧版共存、迁移期间外部写入及触发器重建边界，完善接入验收；保持生产全库代数、自动合并和生产恢复关闭。`
