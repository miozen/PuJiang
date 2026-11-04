# 接力状态

- **Last Updated：** 2026-11-04（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** 同会话人物、关系、婚配及图表选线基础读取已扩展；真实谱系横向投影与 UI 旧结果失效尚未接入。D-014 关闭项不变。
- **Last Completed：** `ProjectionReadSession` 新增单谱父母/子女候选、婚配、失考/有方向配偶关系及图表列表有界分页和单人物图表选线读取；D-022 全库代数变化或关系身份失效时保留旧信息并禁止直接应用。隔离验证多候选、失考、双人和未详婚配、跨谱拒绝及选线失效。详见[横向设计](HORIZONTAL_LINEAGE_DESIGN.md)。没有产品界面调用，真实横向投影和 UI 旧结果失效尚未实施。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows .NET 10 Release 解决方案构建成功（0 错误、31 警告）；只读会话专项 12/12、日常非 UI/Performance 测试 392/392、既有真实主窗口在途读取退出 UI 专项 1/1，均无失败或跳过。隔离 SQLite 覆盖多父母、分页、失考、双人/未详婚配、跨谱拒绝、图表代数变化及关系/人物删除后的历史选线。未访问正式用户数据；未验证新横向窗口、UI 旧结果失效或 Performance。
- **Validation Pending：** 本次代码提交远端 CI 待核实；先前 `eeb845f` CI 结果按用户要求等待用户告知。真实谱系图、关系及婚配的界面展示、选线交互、UI 旧结果失效、用户视觉及性能验收均未完成；会话读取接口尚无产品界面调用。D-028 的具体文本解析和跨支规则仍待确认，D-014 关闭项不变。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 设计并实现 D-028 真实谱系横向窗口与请求生命周期：切谱、取消、版本变化和关闭时清除已显示内容并拒绝在途旧结果；补齐关系对象、明确世代/顺序的会话选择和诊断，再通过隔离 SQLite 与真实 WPF 验证。不得把 ID 游标顺序当谱载排行；遵守 D-014。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025 恢复，实现 D-028 真实横向窗口及 UI 请求失效处理，补齐关系对象和世代/顺序诊断并以隔离 SQLite 与真实 WPF 验证；遵守 D-014，不推断未确认业务规则。CI 结果由用户告知。`
