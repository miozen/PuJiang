# 接力状态

- **Last Updated：** 2026-10-25（Asia/Shanghai）
- **Current Branch：** `dev`（本地新建，尚未推送或设置 upstream）
- **HEAD / Last Commit：** `e725a7b34762ff47f0b98c10acb3e3ffe233cca6` / `e725a7b feat(font): 新增自定义字体设置支持并优化PDF及打印字体渲染`
- **Working Tree：** DIRTY，包含未提交的长期契约、P0 目标模型/Migration、P0-0 解决方案/测试/CI/数据目录/日志基础、应用名称和本地 Git remote 更新。
- **Remote Sync：** `origin` 为 `https://github.com/miozen/PuJiang.git`。本轮只读 `git ls-remote --heads origin` 成功但未返回分支头；本地 `dev` 尚未推送或设置 upstream，因此不能视为已同步。
- **Current Phase：** P0-1 — Migration 与关系完整性基线（完成，等待用户确认下一项 P0 工作）。
- **Current Task：** 无进行中的实施任务；等待首个 WPF CRUD 切换用例的明确授权与范围。
- **Last Completed：** P0-1 已建立 `ZupuDbContextFactory`、`ZupuDbContextModelSnapshot` 和本地 `dotnet-ef` 10.0.12 工具；目标模型新增同谱人物/关系批量写入事务，失败会回滚该批次人物；新增失考、重复关系及事务提交/回滚测试。旧 WPF 原型未切换。
- **Uncommitted Work：** 全部本轮文件和修改均未提交；不得把本地 `dev` 状态视为远端已接力。
- **Validation Performed：** SDK `10.0.402`；Release build 成功（0 error，24 个已知 ImageSharp 漏洞警告）；日常测试（`Category!=Performance`）为 10/10 通过，Performance 样本测试为 1/1 通过；本地 `dotnet-ef` 10.0.12 能无连接识别 `InitialPuJiang`，并确认快照无待迁移模型差异；未启动 WPF 应用，未创建正式用户数据库。最终 diff 检查待本轮文档写入完成后执行。
- **Validation Pending：** GitHub Actions 尚未触发；人工 UI、真实千人布局计时、迁移升级前备份及导入/恢复事务未验证；待远端建立分支或用户授权同步后，为 `dev` 设置 upstream。
- **Known Issues / Blockers：** 原型 WPF 界面仍使用旧 `FamilyMember`；依赖链仍报告 `SixLabors.ImageSharp` 1.0.4 高、中危漏洞（NU1903/NU1902）；传统吊线图规则仍未确认。没有阻塞 P0-1 完成的环境问题。
- **Exact Next Action：** 获得用户对具体用例的授权后，定义并实现首个 WPF CRUD 切换（建议“创建族谱并在当前会话切换”）的最小范围、ViewModel 边界和隔离验收测试；此前不改动 WPF 绑定或导航。
- **Cross-machine Ready：** NO — `dev` 和本轮变更均未获授权提交/推送。
