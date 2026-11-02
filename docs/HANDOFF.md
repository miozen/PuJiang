# 接力状态

- **Last Updated：** 2026-11-02（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 本轮开始时本地和实际远端 `dev` 均为 `38c05b2`；该提交为纯文档，按路径过滤预期不运行 CI，不视为远端通过。后续仍须核对实际远端。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数候选切换评审；自动合并和生产恢复继续关闭。
- **Last Completed：** 以真实 V1 结构在测试工程验证令牌、读取事务及单条删除原型的部分隔离边界，覆盖范围见[架构](ARCHITECTURE.md)；[按谱候选切换方案](SCOPED_CANDIDATE_SWITCH_PROPOSAL.md)仍待批准，生产解除候选仍为全库代数。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** `dotnet build PuJiang.slnx --no-restore --configuration Release` 通过；测试专用候选定向 16/16、日常测试 297/297 通过；使用独立临时数据库，未访问正式用户数据或启动生产恢复。
- **Validation Pending：** 本轮远端 CI；空对方、实际切换/回退在途、外部进程及 WPF 等完整候选验收；按谱生产切换尚未实施或获批；完整备份包和生产恢复仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 继续 P0，在测试专用候选原型补验空对方、策略切换回退和在途竞争；未经明确授权不得切换生产候选，生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，继续 P0，在测试专用候选原型补验空对方、策略切换回退和在途竞争；未获准前保持生产全库代数、自动合并和生产恢复关闭。`
