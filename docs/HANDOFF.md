# 接力状态

- **Last Updated：** 2026-11-04（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2-1 首位子女对齐已修正；用户确认 P2-2 固定虚构样例窗口外观符合预期，展开折叠等交互反馈待确认；D-014 关闭项不变。
- **Last Completed：** 修正 P2-1 父节点对齐首位可见子女；新增多根、四代、混合尺寸、非首房嵌套与展开折叠测试，P2-2 真实 WPF 固定样例检查实际卡片及正交连线坐标、折叠重展。范围见[横向设计](HORIZONTAL_LINEAGE_DESIGN.md)。用户在独立测试目录启动后反馈窗口出现且样子符合预期；未明确确认具体展开折叠动作。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows .NET SDK 10.0.403：`dotnet restore PuJiang.slnx` 成功；`dotnet build PuJiang.slnx --no-restore --configuration Release` 成功（0 错误、31 警告）；日常非 UI/Performance 测试 371/371，失败及跳过均为 0；单独 P2-2 WPF 专项 1/1，失败及跳过均为 0。WPF 专项直接实例化仅含虚构内存数据的窗口，不初始化数据库。未进行用户视觉验收或 Performance 专项。
- **Validation Pending：** 本次代码提交的远端 CI 待实际核实；之前 P2-2 提交 `eeb845f` CI 最后观察为运行中，用户要求不等待，结果待用户告知。用户已确认固定样例窗口外观符合预期，尚未明确反馈多房支展开、折叠及重展；缩放锚点精度、其他设备和性能未专项测量。D-025 世代、排序、复杂配偶等规则尚待确认；真实投影、目标工作台、草稿及按需查询均未实现，完整 P2 尚未验收。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 请用户在已打开的 P2-2 虚构样例展开长房多代与次房，再折叠重展长房，确认首子对齐、整棵可见子树占位和正交连线并记录实际反馈；遵守 D-014，不推断未确认规则。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025 恢复，协助用户确认 P2-2 固定样例的多房支展开折叠和整棵可见子树占位，记录反馈；遵守 D-014，不推断未确认业务规则。CI 结果由用户告知。`
