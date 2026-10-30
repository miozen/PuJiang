# 接力状态

- **Last Updated：** 2026-10-30（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 经 GitHub Actions REST 核对 `2671b70` 的 CI run `40983552482` 为 `success`。本轮开始时实际 `origin/dev` 和本地 HEAD 均为 `2671b70`；后续接力仍须用 `git ls-remote origin refs/heads/dev` 与本机 HEAD 比较，不得依赖旧跟踪引用。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Current Task：** P0 目标关系/婚配的确认候选隔离与数据完整性；自动合并和生产恢复继续关闭。
- **Last Completed：** 隔离 GEDCOM 导入 50 人和一条婚配时，图数据、来源记录及代数 51 次增长同事务提交，使原谱旧候选失效；婚配写入失败则图数据、来源和代数一起回滚，原候选仍有效。另一既有族谱人物修改提交会使旧候选保守失效，回滚不失效。隔离 UI 子进程覆盖外部导入及关系/婚配写入正常退出时的代数与提交一致。250 人虚构 GEDCOM 单机预热样本中，人物插入触发器关闭/开启各 3 次中位数为 74.8/104.7 ms；仅供初步观察，未覆盖正式性能验收。
- **Uncommitted Work：** 以 `git status --short` 为准；仅提交经验证的本轮代码、测试和文档。
- **Validation Performed：** 本轮导入与跨谱定向测试 4/4、单独 UI 正常退出竞争 2/2、性能样本 1/1 通过；日常 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-restore --no-build --filter "Category!=Performance&Category!=UI"` 221/221 通过。性能样本仅针对人物插入触发器，使用独立临时库；未访问正式用户数据库，未启动生产恢复。
- **Validation Pending：** 本轮提交后的远端 CI；生产恢复、完整备份包和真实性能仍未验证。
- **Known Issues / Blockers：** 全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 核对本轮提交的远端 CI；随后继续 P0，评估全库代数在更大虚构样本、关系/婚配写入及跨谱并发下的性能与误失效，确定是否值得设计更细粒度方案；保持自动合并和生产恢复关闭。
- **Cross-machine Ready：** 提交并推送后，须按本文件开头的 Git 状态与实际远端核实。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给下一台电脑的 Codex：`按 AGENTS.md 和 docs/HANDOFF.md 恢复，确认本轮 CI；继续 P0，评估全库代数在更大虚构样本、关系/婚配写入及跨谱并发下的性能与误失效，确定是否值得设计更细粒度方案；保持自动合并和生产恢复关闭。`
