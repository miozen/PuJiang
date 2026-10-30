# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** `93327be7674db477a9896a43fd3f0cc8d869fc32` 已推送至 `origin/dev`；用户已确认其前一 `5f64198` Windows CI 为 green。`458fe8d` 与 `93327be` 的 Windows CI 未在本轮主动轮询；仅在下一个代码检查点或用户请求时实际核对。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数正式 Migration 的真实实现前验收设计：以 V1 SQL 契约定义 EF Migration、锁失败报告、旧版共存、触发器重建和严格备份兼容的隔离测试；自动合并和生产恢复继续关闭。
- **Last Completed：** 已固定按谱正式 Migration V1 实现设计：两张无外键活动/墓碑表、11 个不可变触发器、事务化 Up/Down、15 秒 `BUSY`/`LOCKED` 失败报告和受保护表的双轨触发器重装。设计未部署，未改变生产全库候选路径。详见[V1 实现设计](SCOPED_UNLINK_GENERATION_V1_DESIGN.md)。
- **Uncommitted Work：** 仅既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除。
- **Validation Performed：** 用户确认 `5f64198` 的 GitHub Windows CI 已绿；`93327be` 的本机 `dotnet build PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore` 通过（0 error）。本轮完成文档设计并以现有全库 Migration、按谱原型、迁移服务和预检源码交叉核对；未重复运行测试、未访问正式用户数据库，也未启动生产恢复。
- **Validation Pending：** `458fe8d` 的备份双向拒绝/触发器重装及 `93327be` 的旧模型启动/安装中断用例的 Windows CI 未主动轮询；V1 的真实 EF Migration、模型快照、15 秒锁错误报告、重建重装和正式生产预检双向拒绝仍未实现或验收。生产恢复、完整备份包、完整图读取性能及预检清理本身失败或进程终止仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 继续 P0，按 V1 实现设计先实现真实 EF Migration 与 `ScopedUnlinkGenerationV1` 常量，并以隔离 SQLite 验收旧库升级/回滚、15 秒锁报告、旧模型写入、表重建重装和正式备份双向拒绝；在该检查点才运行日常测试并核对 CI。生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，在隔离环境评估按谱草案的旧版共存、迁移期间外部写入及触发器重建边界，完善接入验收；保持生产全库代数、自动合并和生产恢复关闭。`
