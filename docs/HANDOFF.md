# 接力状态

- **Last Updated：** 2026-11-04（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2 真实谱系只读投影的世代、同胞排序与复杂关系规则已形成建议，待用户确认；D-014 关闭项不变。
- **Last Completed：** 对照 Accepted 决策、人物/关系/婚配模型、完整快照、主线会话、图表配置及横向布局，形成[真实谱系只读投影实施前评估](HORIZONTAL_LINEAGE_DESIGN.md)：列出已有能力、待用户确认的业务选项、按需一致性、实施顺序和验收范围。仅为 Proposed，未改生产代码或决策。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 本轮为文档评估，核对相关源码、契约及 `git diff --check`；未运行构建、测试或访问用户数据库。此前 Windows Release 构建通过、日常测试 371/371 与 WPF 固定样例专项 1/1；用户确认固定样例的对齐、占位及连线符合预期，细节见 Git 历史。
- **Validation Pending：** 本次代码提交的远端 CI 待实际核实；之前 P2-2 提交 `eeb845f` CI 最后观察为运行中，用户要求不等待，结果待用户告知。用户反馈仅覆盖固定样例；缩放锚点精度、其他设备和性能未专项测量。D-025 世代、排序、复杂配偶等规则尚待确认；真实投影、目标工作台、草稿及按需查询均未实现，完整 P2 尚未验收。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 请用户确认[真实谱系只读投影的世代列、同胞顺序及复杂关系建议](HORIZONTAL_LINEAGE_DESIGN.md)；明确待定细节后先记录 Accepted 决策，再细化并实施获确认范围。遵守 D-014。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025 恢复，确认横向设计中真实谱系投影的世代、排序及复杂关系建议，先记录决策，再实施获确认范围；遵守 D-014，不推断未确认业务规则。CI 结果由用户告知。`
