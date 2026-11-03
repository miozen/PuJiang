# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2-2 横向只读画布固定样例切片；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 主窗口“视图”菜单新增独立 WPF 横向图示例，使用固定虚构内存数据复用 P2-1 纯布局；选择、直接子女展开、独立房支折叠、占位配偶、搜索定位、鼠标滚轮缩放、滚动和空白拖动均不写入数据库，旧入口保留。真实族谱投影和统一工作台未接入。先前图表复核可读性任务仍保留。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows Release 解决方案构建成功；固定样例 WPF 专项 1 项及日常回归 370 项通过，均无失败或跳过。使用专用 `artifacts/p2-horizontal-visual-20261103` 测试目录启动程序，助手在实际窗口观察四代同列、正交连线及展示，并操作选择、其他分支保留、折叠、右键不展开、空白取消、缩放、搜索及放大后空白拖动平移；未访问正式用户数据库。`--no-restore` 复用还原，`--no-build` 在相同源码 Release 构建后使用。P2-1 原代码提交 `3a30217` 的 [GitHub CI #143](https://github.com/miozen/PuJiang/actions/runs/41655449379) 通过。
- **Validation Pending：** 本次 P2-2 代码提交的远端 CI 待核实；用户本人视觉验收未进行，上述为助手实际操作观察，不等同用户确认。缩放锚点精度、其他设备和性能未专项测量；D-025 世代、排序、复杂配偶等规则尚待确认；真实投影、目标工作台、草稿及按需查询均未实现。完整 P2 尚未验收；D-014 关闭项不变。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本次代码提交对应 CI；然后继续 P2-2，先定向设计真实谱系到显式世代列与同胞顺序的只读投影及缺失/冲突诊断，不推断未确认规则，遵守 D-014。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025/D-026 恢复，核对样例代码 CI，定向设计 P2-2 真实谱系的只读世代/顺序投影及缺失冲突诊断；不推断未确认规则，遵守 D-014。`
