# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 为准；本次连接生命周期工作基于 `a005583`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 前一检查点 `a005583` 已推送到 `origin/dev`；本轮连接生命周期改动的提交和推送状态以实际 Git 检查为准。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Last Completed：** 主窗口目标和旧模型服务共用协调器；旧 `DataService` 每次读写使用并释放短生命周期上下文，预览/协作导入复用该服务；启动迁移、旧批量导入和数据库备份创建登记外层操作；隔离预检仍使用临时副本。生产恢复未接入。
- **Validation Performed：** 本机 Windows SDK 10.0.402；`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --no-restore --configuration Release --filter "Category!=Performance"` 为 113/113 通过，含旧服务读写/排空、真实 SQLite 迁移及备份与排空竞争；未访问正式用户数据库。NuGet 漏洞索引本轮无法访问；已有 ImageSharp 告警仍需处理。
- **Validation Pending：** 未启动 WPF，实际主窗口/预览/导入/备份 UI 与排空协同需隔离环境验证；备份预检和设计时上下文不属于当前库操作。外部写入、真实 WAL 收敛、崩溃和生产恢复尚未覆盖；完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在独立测试数据目录中验证实际主窗口、预览、导入与备份在排空时的资源释放和失败行为，并设计应用级排空接入；继续保持生产恢复关闭。
- **Cross-machine Ready：** 须确认工作区干净且远端 `dev` 已包含本检查点；本机 `ZupuPaiBan/App.xaml` 有此前同步留下的行尾差异，不属于本次检查点。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内的恢复协调器、阶段记录及停用闸门原型目前**仅在测试工程**；实际应用尚未统一管理所有数据库连接，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，先核对 dev、工作区和实际远端，再按 Exact Next Action 继续；验收通过后检查并及时同步，按六部分格式汇报。`
