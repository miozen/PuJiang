# 接力状态

- **Last Updated：** 2026-11-03（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2 交互世系浏览；自动合并、生产恢复和按谱候选切换继续关闭。
- **Last Completed：** 三谱形隔离 WPF 补充视口边界、缩放、路径、折叠、切谱和配置失效服务检查，记录 20 次布局耗时；检查离屏图像并确认保持启动会话可显示独立测试窗口，范围及限制见 [视口测量](P2_VIEWPORT_2026-11-03.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows Release 解决方案构建通过（0 错误、16 警告），日常非 UI/Performance 349/349，三谱形扩展专项 3/3 通过；早期两轮跨边界测试位置假设不成立，修正选点后通过。此前功能代码 `d4ff82f` GitHub Actions run `41629852241` 通过；本轮测试代码 CI 以新提交实查。未访问正式用户数据库。详见 [视口测量](P2_VIEWPORT_2026-11-03.md)。
- **Validation Pending：** 三谱形配置失效只在服务层逐一验证，窗口复核使用既有小图测试；实时图形人工视觉尚未执行，宽分支密集连线需由人确认可读性。全展开通过测试工具预设，非产品入口。生产恢复与完整备份包未完成，候选切换和相关深层专项暂停。ImageSharp 已知告警仍在。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在独立测试数据中完成三谱形实时人工视觉及配置窗口复核，确认宽分支密集连线的可读性；遵守 D-014，未完成前不宣称 D-024 或完整 P2 验收通过。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和最新 HANDOFF 恢复，使用独立测试数据完成 D-024 三谱形实时人工视觉和配置窗口复核；遵守 D-014。`
