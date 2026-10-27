# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 为准；本次外层操作许可工作基于 `fe5b0a5`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 前一检查点 `fe5b0a5` 已推送到 `origin/dev`；本轮后续操作许可改动的提交和推送状态以实际 Git 检查为准。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Last Completed：** 主窗口目标族谱 ViewModel 的异步业务用例已登记外层许可，允许已登记操作在排空期间完成嵌套上下文访问；拒绝独立新操作并防止操作等待自身排空。旧模型与生产恢复界面未改动。
- **Validation Performed：** 本机 Windows SDK 10.0.402；`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --no-restore --configuration Release --filter "Category!=Performance"` 为 111/111 通过，含真实 SQLite 下多步骤创建/加载/选择与排空竞争、防自身排空；未访问正式用户数据库。NuGet 漏洞索引本轮无法访问；已有 ImageSharp 告警仍需处理。
- **Validation Pending：** 协调器只覆盖目标 ViewModel，旧模型、启动迁移、预览、导入及备份仍未登记；不可据此启动生产恢复。外部写入、真实 WAL 收敛及进程崩溃未覆盖。生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 审计并设计旧模型、启动迁移、预览、导入和备份等未登记入口的统一生命周期及停用边界，添加真实资源释放和并发测试；所有入口完成登记或隔离之前继续禁止生产恢复。
- **Cross-machine Ready：** 须确认工作区干净且远端 `dev` 已包含本检查点；本机 `ZupuPaiBan/App.xaml` 有此前同步留下的行尾差异，不属于本次检查点。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内的恢复协调器、阶段记录及停用闸门原型目前**仅在测试工程**；实际应用尚未统一管理所有数据库连接，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，先核对 dev、工作区和实际远端，再按 Exact Next Action 继续；验收通过后检查并及时同步，按六部分格式汇报。`
