# 接力状态

- **Last Updated：** 2026-10-27（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 为准；本验收检查点基于 `dc75b0f243d810e77375ac50090a83da8d47b9da`。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 本检查点提交前核实远端 `dev` 为 `dc75b0f243d810e77375ac50090a83da8d47b9da`；当前同步状态以实际远端及 Git 状态核实。
- **Current Phase：** P0 — 开发基础与数据模型。
- **Last Completed：** 备份界面增加恢复前预检按钮和范围报告；用户确认预检成功及范围、无效包提示、取消及当前族谱数据不变三组人工验收通过。
- **Validation Performed：** Windows SDK 10.0.403；使用 `-p:BaseOutputPath=D:\20_Projects\PuJiang\artifacts\backup-preflight-ui-verification\` 执行日常测试，68/68 通过；用户确认在独立目录 `artifacts/ui-test-p0-preflight-20261027` 的三组人工验收通过；未访问正式用户数据库。
- **Validation Pending：** 预检严格匹配当前迁移及结构，不支持旧版本或自定义结构；备份不包含附件和配置，恢复未实现，哈希不能证明来源真实性。真实性能、Performance、远端 CI、完整备份包和导入/恢复事务亦未完成。
- **Known Issues / Blockers：** 独占锁不能阻止旧版本或第三方直接写库；SQLite 快照仅含数据库。旧成员树、导入与输出不受目标族谱选择影响；旧导入缺少预检和事务。ImageSharp 1.0.4 存在已知高、中危告警。
- **Exact Next Action：** 基于已验收预检设计数据库恢复的安全实施方案，明确停用连接、独占锁、恢复前备份、原子替换、失败回滚与重启边界，先补充隔离测试，不触碰真实用户数据库。
- **Cross-machine Ready：** 须确认工作区干净且远端 `dev` 已包含本检查点。
