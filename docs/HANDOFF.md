# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P1 完整族谱管理；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 有方向配偶关系在对方人物清单中只读展示原记录方向、ID、备注及来源，可按 ID 定位原主体；不创建反向记录或合并婚配。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 复用有效还原的 Release 解决方案构建通过（0 错误，31 条告警）；日常非 UI/Performance 回归 339/339，隔离 SQLite 与真实 WPF 窗口定向测试 2/2 通过。覆盖双方查看定位、同名 ID、独立婚配并存及解除隔离、切谱/刷新旧条目拒绝；未访问正式用户数据库。此前两个恢复原型子进程超时的根因未确定。
- **Validation Pending：** 本轮检查点远端 CI 待核实；本机未执行人工视觉验收和 Performance 专项，生产恢复与完整备份包未完成；候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 评估 P1 剩余关系维护用例及可操作验收，提出推荐方案并遵守 D-014；更换既有关系双方需另行明确历史记录语义。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，评估 P1 剩余关系维护用例并提出推荐方案，遵守 D-014。`
