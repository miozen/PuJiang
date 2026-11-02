# 接力状态

- **Last Updated：** 2026-11-02（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 本轮开始时从 `824ed65` 快进至实际远端 `d7d58ec`；GitHub Actions `824ed65` 的 run `41027016849` 和 `d7d58ec` 的 run `41076692548` 均为 `success`。后续仍须核对实际远端。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 按谱代数 V1 Migration 已完成接入前兼容、WPF 和退出隔离验收；下一步是独立评审候选读取/解除是否获准切换。自动合并和生产恢复继续关闭。
- **Last Completed：** 继承 V1 双轨 Migration 与前序隔离验收；本轮补验关系表和婚配表重建丢失双轨守卫及事务重装，限制见[架构](ARCHITECTURE.md)。生产解除候选仍为全库代数。详见[V1 实现设计](SCOPED_UNLINK_GENERATION_V1_DESIGN.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 原有按谱原型及 V1 隔离测试 53/53 通过；新增重建测试 2/2 通过；日常 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore --no-build --filter "Category!=Performance&Category!=UI"` 281/281 通过。未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的 CI；按谱候选读取/解除正式切换尚未实施或获批；完整备份包、生产恢复、完整图读取性能及预检清理本身失败或进程终止仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 继续 P0，独立评审按谱候选读取/解除是否具备从全库代数切换的条件；若获准，再先固定双轨切换/回退边界、候选读取与单条删除的按谱 guard 契约及隔离验收，之后才可实施。生产仍用全库代数，自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，独立评审按谱候选读取/解除的切换条件，未获准前保持生产全库代数、自动合并和生产恢复关闭。`
