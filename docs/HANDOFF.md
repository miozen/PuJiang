# 接力状态

- **Last Updated：** 2026-10-28（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** `219d324` 已于 2026-10-28 与 `origin/dev` 一致；本轮导入追溯检查点尚待提交和推送。后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 `HEAD` 比较确认，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** 内部 JSON 导入来源与格式版本追溯已实现并验证，待提交同步检查点。
- **Last Completed：** 成功的内部 v1 JSON 导入会在创建新族谱及全部图记录的同一 SQLite 事务中持久记录来源种类 `PuJiang.InternalJson`、格式版本、来源族谱 ID、规范化 JSON SHA-256 指纹与导入时间；拒绝和写入失败不会留下来源记录。记录按 `GenealogyId` 隔离，不含本机路径。未接入 WPF、未支持副本 ID 重映射或自动合并，也未接入生产恢复。
- **Uncommitted Work：** 导入来源追溯实体、EF Migration、服务与隔离 SQLite 测试及对应契约文档；另有本机既有 `ZupuPaiBan/App.xaml` 行尾差异，必须排除在提交之外。
- **Validation Performed：** Windows 原生 .NET 使用每例独立临时目录。最终源码下内部 JSON 导入专项 8/8 通过（来源记录字段、按谱只读查询、同名不同 ID 导入、来源 ID 冲突拒绝、无效引用拒绝及写入失败完整回滚）；EF 工具报告无待生成 Migration。此前 `Category!=Performance&Category!=UI` 为 131/131 通过；在最终增加只读查询后的两次同一日常集尝试均在测试宿主汇总前结束，未获得可声明的最终全量结果。未访问正式用户数据库，未启动生产恢复。NuGet 漏洞索引不可访问；存在既有 ImageSharp 1.0.4 高、中危告警。
- **Validation Pending：** 在可稳定运行的 Windows 测试宿主重新执行最终源码的 `Category!=Performance&Category!=UI`；本轮 GitHub Actions；内部 JSON 的副本 ID 重映射与 WPF 操作入口；完整 Excel/GEDCOM 解析与外部格式预检；`Application.Shutdown`、系统关机、强制终止、外部写入、真实 WAL 收敛和崩溃边界；生产恢复、完整备份包、真实性能与远端 CI 尚未验证。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；内部 JSON 尚无副本导入或 WPF 入口，外部格式仍未映射到目标模型。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对最终 diff、测试输出与远端 `dev` 后，提交并推送本导入来源追溯检查点；随后在 P0 设计内部 JSON 的显式副本 ID 重映射边界，继续保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`请按 AGENTS.md 和 docs/HANDOFF.md 恢复接力，核对 dev、工作区和实际远端，确认内部 JSON 预检检查点已同步；保持生产恢复关闭并继续 P0。`
