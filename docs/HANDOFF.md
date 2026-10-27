# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 为准；本跨电脑接力检查点基于 `ae93629bd4ca23f998ea7cda387a61e045a2ce92`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 接力整理前通过 GitHub 连接器核实远端 `dev` 为 `ae93629bd4ca23f998ea7cda387a61e045a2ce92`；本接力文档提交后的状态需再次核实实际远端及 Git 状态。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Last Completed：** 审计主窗口长期上下文、预览及导入的直接上下文、目标模型短生命周期上下文、备份和连接池；记录统一登记、拒绝新操作、排空、释放及失败关闭的待实施方案。测试工程新增停用闸门原型，未接入应用。
- **Validation Performed：** Windows SDK 10.0.403；使用 `-p:BaseOutputPath=D:\20_Projects\PuJiang\artifacts\quiescence-verification\` 执行日常测试，106/106 通过，含真实上下文与连接池释放、并发操作排空、关闭失败、取消以及未登记上下文可绕过闸门；未访问正式用户数据库。
- **Validation Pending：** 应用未接入该原型且存在未集中管理的上下文，不可据此启动生产恢复；原型不检测外部写入、真实 WAL 收敛或进程崩溃。此前进程结束后锁释放延迟原因未确定。生产恢复尚未实现，备份不包含附件和配置；真实性能、Performance、远端 CI、完整备份包和导入/恢复事务亦未完成。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 按连接生命周期审计结果，限定目标模型用例设计并验证可登记、可排空的上下文管理边界；保持旧模型及生产恢复界面不变，明确未接入入口仍不允许恢复。
- **Cross-machine Ready：** 须确认工作区干净且远端 `dev` 已包含本检查点。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内的恢复协调器、阶段记录及停用闸门原型目前**仅在测试工程**；实际应用尚未统一管理所有数据库连接，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，先核对 dev、工作区和实际远端，再按 Exact Next Action 继续；验收通过后检查并及时同步，按六部分格式汇报。`
