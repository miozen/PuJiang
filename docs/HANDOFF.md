# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P1 — 完整族谱管理。
- **Current Task：** P1 关系维护评估：建议补齐有方向配偶关系在对方人物清单中的只读展示；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 生物父母关系快捷解除绑定所选父母与子女的唯一同谱候选，复用记录预览、确认和全库代数守卫；确认期间选择或记录变化时拒绝。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** 复用有效还原的 Release 解决方案构建通过（0 错误，23 条告警）；日常非 UI/Performance 回归 338/338，真实 WPF 窗口专项 1/1 通过。隔离 SQLite 覆盖同名、跨谱、选择往返、记录修改、改名及同 ID 删除重插后的旧候选拒绝，真实窗口覆盖预览、取消及确认；未访问正式用户数据库。此前两个恢复原型子进程超时的根因未确定。
- **Validation Pending：** 本轮检查点远端 CI 待核实；本机未执行人工视觉验收和 Performance 专项，生产恢复与完整备份包未完成；候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 用户确认后，为已有有方向的 `PersonRelationship.Spouse` 在对方人物清单展示指回原主体的只读条目，明确原记录方向、关系 ID、备注/来源和对方定位；不新增反向记录，不合并或推导婚配，也不改变解除与编辑语义。验收覆盖双方查看与定位、同名人物 ID、原记录资料和方向、与独立婚配并存时各自显示及解除不相互影响、切谱/刷新旧条目失效和零写入；隔离 SQLite 与真实 WPF 窗口验证，遵守 D-014。更换既有关系双方需另行明确历史记录语义。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复；用户确认后实施有方向配偶关系的对方只读展示与定位，完成隔离验收，遵守 D-014。`
