# 接力状态

- **Last Updated：** 2026-11-04（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** D-028 真实谱系只读投影及生产级分批一致性尚未完成；SQLite 连接级版本已完成有限隔离原型。D-014 关闭项不变。
- **Last Completed：** 新增测试专用 SQLite `data_version` 原型；在迁移库验证 7 个真实依赖表的其他连接提交、只读、回滚及 WAL 快照，详见[横向设计](HORIZONTAL_LINEAGE_DESIGN.md)。未接入生产或真实谱系投影；分批首读竞态及生命周期仍未验证。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows Release 解决方案构建成功（0 错误、23 警告）；`ProjectionReadVersionPrototypeTests` 3/3，日常非 UI/Performance 测试 374/374，失败及跳过均为 0。最初构建因此前独立测试实例占用 exe/dll 失败；用户关闭窗口且助手正常关闭自身隔离实例后重新构建成功。全部数据库为测试临时文件；未触碰正式数据。本轮未运行 UI、Performance 或人工验收。
- **Validation Pending：** 本次代码提交的远端 CI 待实际核实；之前 P2-2 提交 `eeb845f` CI 最后观察为运行中，用户要求不等待，结果待用户告知。用户反馈仅覆盖固定样例；缩放锚点精度、其他设备和性能未专项测量。D-028 已确认一般处理原则，具体谱载文本解析、跨支同画布和图表保存细节仍待确认；真实投影、目标工作台、草稿及按需查询均未实现，完整 P2 尚未验收。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 完成只读连接版本检查与首个真实数据读取的竞态、数据库文件身份/替换、连接关闭重建、取消和退出排空的设计与隔离验证；确认安全后实现分批查询并接入 D-028 真实谱系只读投影。不得将测试原型或 `data_version` 数值视为跨连接持久版本；遵守 D-014。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按 AGENTS.md、最新 HANDOFF 和 D-025 恢复，继续验证 data_version 原型的首读竞态和数据库生命周期，再实现按需查询及 D-028 真实谱系只读投影；遵守 D-014，不推断未确认业务规则。CI 结果由用户告知。`
