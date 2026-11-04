# 接力状态

- **Last Updated：** 2026-11-04（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** 新增只读会话及按谱人物分页基础组件，真实谱系横向投影与 UI 失效处理尚未接入；D-014 关闭项不变。
- **Last Completed：** 新增 `ProjectionReadSession`，使用只读独立连接、会话令牌、版本检查、串行分页、本机文件租约和跨谱过滤；协调器排空先取消全部已登记会话并等待释放。隔离测试覆盖变更后旧令牌、关闭重建、取消及真实 SQL 查询在途排空；真实 WPF 主窗口退出专项通过。详见[架构](ARCHITECTURE.md)与[横向设计](HORIZONTAL_LINEAGE_DESIGN.md)。无产品界面使用该组件，真实横向投影仍未实施。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows .NET 10 Release 解决方案构建通过（0 错误、31 警告）；`ProjectionReadSessionTests` 10/10，日常非 UI/Performance 测试 390/390，真实主窗口正常退出 UI 专项 1/1，均无失败或跳过。SQLite 测试函数在真实查询执行期间暂停，取消后排空等待查询返回并丢弃结果；不保证任意 SQL 立即中断。所有数据库为独立临时测试文件，未访问正式用户数据。未进行用户视觉或 Performance 验收。
- **Validation Pending：** 本次代码提交远端 CI 待核实；先前 `eeb845f` CI 结果按用户要求等待用户告知。真实谱系图、按需关系和婚配、图表选线、UI 旧结果失效、用户视觉及性能验收均未完成；会话组件尚无产品界面调用。D-028 的具体文本解析和跨支规则仍待确认，D-014 关闭项不变。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 扩展按需父母候选、婚配、图表选线及问题诊断的同会话读取；实现并隔离验证 UI 在切谱、取消、变更及关闭时丢弃旧结果，随后接入 D-028 的真实谱系横向投影。遵守 D-014，不推断未确认排行解析或跨支规则。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025 恢复，扩展会话按需读取父母候选、婚配与图表选线，验证 UI 旧结果失效并接入 D-028 真实谱系横向投影；遵守 D-014，不推断未确认业务规则。CI 结果由用户告知。`
