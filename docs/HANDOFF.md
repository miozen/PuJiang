# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 为准；本预检检查点基于 `43256db4147aa7eab22940a8bb06a3211e8ac73d`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 本检查点提交前通过 GitHub 连接器核实远端 `dev` 为 `43256db4147aa7eab22940a8bb06a3211e8ac73d`；当前状态以实际远端及 Git 状态核实。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Last Completed：** 新增数据库备份恢复预检服务，隔离副本并检查 SQLite 完整性、外键、迁移版本及当前结构，报告数据库、附件和配置覆盖范围；失败与清理失败保留诊断，不替换当前库。
- **Validation Performed：** Windows SDK 10.0.403；使用 `-p:BaseOutputPath=D:\20_Projects\PuJiang\artifacts\backup-preflight-verification\` 执行日常测试，67/67 通过；覆盖合法包、损坏、未知版本、结构差异、源库不变、清理失败及同时存在的原始异常；未访问正式用户数据库。
- **Validation Pending：** 恢复预检尚无 UI，严格匹配当前迁移及结构，不支持旧版本或自定义结构；备份不包含附件和配置，恢复未实现，哈希不能证明来源真实性。真实性能、Performance、远端 CI、完整备份包和导入/恢复事务亦未完成。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 在数据库备份界面接入恢复预检与范围报告，保持不替换当前数据库，并在独立测试目录人工验收成功、无效包与取消行为。
- **Cross-machine Ready：** 须确认工作区干净且远端 `dev` 已包含本检查点。
