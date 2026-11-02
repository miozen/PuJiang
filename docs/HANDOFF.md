# 接力状态

- **Last Updated：** 2026-11-02（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 本轮开始实测本地和实际远端 `dev` 均为 `6ae3c14`；后续以实际远端核对为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P1 谱载世代与排行的查看和维护；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 根据 D-014 启动 P1，完成人物搜索定位并评估剩余功能；下一项及验收范围见 [P1 计划](CURRENT_PLAN.md#p1--完整族谱管理)。历史门闸和按谱方案保留，生产解除候选仍为全库代数。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 本轮评估核对了模型、现有保存和编辑界面，并执行文档差异检查；未改代码或重跑测试。此前 `6ae3c14` 的 Release 构建通过（0 错误，31 条既有告警），日常回归 314/314、独立 UI 定向测试 1/1 通过；未访问正式用户数据或启动生产恢复。
- **Validation Pending：** 本轮远端 CI、人工视觉验收和 Performance 专项未验证；生产恢复与完整备份包未完成，真实数据正式使用前须专项验收。候选切换和相关深层专项暂停，不作为自动下一任务。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 按 [P1 计划](CURRENT_PLAN.md#p1--完整族谱管理)实施人物谱载世代和排行的查看、编辑及可操作验收；不推导主线或修改推导值。按 D-014 不自动恢复 P0 门闸、按谱切换及极端并发专项，生产仍用全库代数。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，实施 P1 人物谱载世代与排行的查看、编辑和可操作验收；遵守 D-014，保持生产全库代数及各项关闭状态。`
