# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 为准；本次导入与关闭竞争工作基于 `afe1f76`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 前一检查点 `afe1f76` 已推送到 `origin/dev`；本轮改动的提交和推送状态以实际 Git 检查为准。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Last Completed：** 隔离 UI 测试以预先指定的虚构文件执行协作导入，在真实 SQLite 插入暂停期间排空并关闭主窗口，验证在途导入完成前排空等待、新备份提交被拒、测试显式持有的目录锁及数据库句柄释放；已记录子窗口关闭协调设计。生产恢复未接入。
- **Validation Performed：** 本机 Windows SDK 10.0.402；`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --no-build --no-restore --configuration Release --filter "Category=UI"` 为 2/2 通过；日常测试（`Category!=Performance&Category!=UI`）为 114/114 通过；使用临时虚构数据库，未访问正式用户数据库。NuGet 漏洞索引本轮无法访问；已有 ImageSharp 告警仍需处理。
- **Validation Pending：** 新测试未操作真实打开文件对话框或导入确认消息框；测试锁由测试显式持有，未证明 `App.OnExit` 在在途操作完成前保留锁。子窗口拒绝关闭、后台任务、超时、取消及应用级退出协调尚未验证。外部写入、真实 WAL 收敛、崩溃和生产恢复尚未覆盖；完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 实现并在隔离进程验证应用关闭期间的数据目录锁与在途操作协调，覆盖子窗口拒绝关闭、取消和超时；在这些边界完成前继续保持生产恢复关闭。
- **Cross-machine Ready：** 须确认工作区干净且远端 `dev` 已包含本检查点；本机 `ZupuPaiBan/App.xaml` 有此前同步留下的行尾差异，不属于本次检查点。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内的恢复协调器、阶段记录及停用闸门原型目前**仅在测试工程**；实际应用尚未统一管理所有数据库连接，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，先核对 dev、工作区和实际远端，再按 Exact Next Action 继续；验收通过后检查并及时同步，按六部分格式汇报。`
