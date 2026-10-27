# 当前开发计划

阶段状态以实际源码与验证为准；本计划不表示功能已完成。决策依据见 [DECISIONS.md](DECISIONS.md)，架构边界见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## P0-0 — 长期开发基础设施建设

- **Goal：** 建立可重复构建、隔离验证、持续集成与本地数据安全的 Windows 开发基线。
- **Scope：** 解决方案入口、SDK 固定、xUnit/SQLite 测试隔离、固定虚构样本、测试分类、Windows CI、数据目录与日志基础、发布与人工验收规范。
- **Completed：** 已建立 `PuJiang.slnx`、`global.json`、`PuJiang.Tests` 和 `.github/workflows/ci.yml`；SQLite 测试每例使用独立临时文件且禁用连接池，当前 6 项测试通过；固定三代和 1,000 人虚构样本已加入；已配置 Unit/Integration/Layout/Performance 分类；已实现 `%LOCALAPPDATA%\\PuJiang` 数据目录抽象、基础轮转日志与未处理异常记录；已补充 `.gitignore`、操作规范和发布目标。
- **Remaining：** 此段记录 P0-0 当时的基线；当前 GitHub Actions 状态需实际核实，后续 UI、性能、备份恢复及工具链进展见下方 P0 和接力状态。
- **Acceptance Criteria：** Windows .NET 10 构建成功；统一解决方案和可重复的隔离 SQLite 测试存在；日常 CI 使用 Windows runner；数据、Migration、备份与发布边界有明确规范；不触碰真实用户数据库。
- **Validation：** `dotnet build PuJiang.slnx --no-restore --configuration Release` 成功（0 error，16 个既有 ImageSharp 安全警告）；日常测试（排除 Performance）为 5/5 通过，Performance 样本测试为 1/1 通过；`dotnet publish ZupuPaiBan/ZupuPaiBan.csproj -c Release -r win-x64 --self-contained true` 成功并仅输出到已忽略的 `artifacts/publish-smoke`；未启动 WPF 应用，未创建正式用户数据库；远端 CI 未触发。
- **Blockers：** 无本轮实施阻塞。ImageSharp 1.0.4 的高/中危告警需在依赖升级专项处理，不能在此基础设施轮次静默升级。
- **Next Action：** P0-1 已开展，当前唯一下一步见 [接力状态](HANDOFF.md)。

## P0-1 — Migration 与关系完整性基线

- **Goal：** 让目标模型的结构演进可由 EF Core 工具重复验证，并确保批量人物/关系写入具备明确事务边界。
- **Scope：** Migration model snapshot、设计时 DbContext、本地 EF 工具、模型一致性检查、事务提交/回滚和关键关系完整性测试；不切换 WPF UI。
- **Completed：** 已新增 `ZupuDbContextFactory`、`ZupuDbContextModelSnapshot` 和固定版本的本地 `dotnet-ef` 工具；已新增同谱人物/关系批量写入事务，关系校验失败时回滚该批次暂存人物；已覆盖失考合法、重复关系拒绝、事务提交/回滚。
- **Remaining：** 完整备份恢复、导入事务和后续 WPF CRUD 用例仍属 P0 工作；迁移前数据库快照及首个族谱用例进展见下方 P0。
- **Acceptance Criteria：** EF 工具在不访问用户数据库的情况下可识别迁移并确认当前模型无待迁移差异；关键关系写入的成功与失败路径均有隔离 SQLite 测试；不改变现有 WPF UI。
- **Validation：** Release build 成功（0 error）；日常测试 10/10 通过，Performance 样本 1/1 通过；`dotnet tool run dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` 报告无模型差异。
- **Blockers：** 无。ImageSharp 安全告警与 UI/备份/导入工作不在本任务范围。
- **Next Action：** 首个族谱创建及会话切换用例已进入 P0，后续状态见下方 P0。

## P0 — 开发基础与数据模型

- **Goal：** 建立可安全演进、可测试的多族谱领域与本地持久化基础。
- **Scope：** Windows 原生构建验证；开发规范与接力机制；新族谱数据模型；多族谱隔离；人物、亲属、婚姻、继嗣及房支模型；EF Core Migration；完整性校验；基础备份恢复；自动化测试基础。
- **Completed：** 本仓库已具备 WPF/EF Core/SQLite 原型；已建立开发协议、决策、架构、计划与接力档案及 P0-0 工程基线。已新增目标模型、复合外键隔离、首个 Migration、关系校验与测试；批量关系校验涵盖同批次和已有关系构成的循环及重复失考。新增按单谱读取人物、关系、婚配和房支的不可变原始数据快照及目标人物只读关系清单，不选择展示主线。WPF 已接入目标族谱创建、会话内切换、按谱人物列表、姓名及基础档案编辑、无关系人物确认删除，以及生物子女新增、已有子女关联、列表和生物父母关系确认解除；旧 `FamilyMember` 功能独立保留。已有数据库有待执行迁移时，先生成并校验 SQLite 数据库快照；新版本实例持有数据目录独占锁。
- **Remaining：** 将 WPF CRUD 切换到目标模型；完整备份包、恢复、导入/恢复事务和更多完整性测试。
- **Acceptance Criteria：** 两套族谱能在同一库安全隔离；关系模型可保留原始谱载与主线投影；非法跨谱/循环关系被拒绝；迁移、备份恢复和关键校验有自动化测试。
- **Validation：** 2026-10-27 Windows SDK 10.0.403；隔离输出目录 Release 日常测试 106/106 通过，含停用闸门测试原型的排空、拒绝新操作、真实上下文释放、连接池清理、关闭失败和取消。测试明确证明未登记上下文可绕过原型；应用尚未接入统一停用，不能视为生产恢复安全。附件、配置与实际恢复未实现，性能和远端 CI 未验证。依赖存在已知 ImageSharp 漏洞警告。
- **Blockers：** 吊线图规则不阻塞 P0；目标实体的字段级方案可在 Accepted 决策范围内自主设计。
- **Next Action：** 按连接生命周期审计结果，限定目标模型用例设计并验证可登记、可排空的上下文管理边界；保持旧模型及生产恢复界面不变，明确未接入入口仍不允许恢复。

## P1 — 完整族谱管理

- **Goal：** 让用户创建、切换并安全维护多套族谱。
- **Scope：** 多族谱创建/切换；人物档案；亲属与谱系关系编辑；世代、排行、房支管理；搜索定位；安全删除和关系保护。
- **Completed：** 当前仅有单树成员 CRUD 原型。
- **Remaining：** 全部目标功能。
- **Acceptance Criteria：** 多谱隔离、关系编辑/保护、原始与推导世代排行并存、搜索定位和安全删除均有测试。
- **Validation：** 未开始。
- **Blockers：** 依赖 P0 数据模型、校验和迁移。
- **Next Action：** P0 完成并验收后，基于目标模型设计族谱切换与人物关系编辑用例。

## P2 — 交互式世系图

- **Goal：** 提供可查询、可展开、可定位的大规模世系浏览。
- **Scope：** 谱系查询投影；逐代展开/折叠；搜索定位与路径高亮；千人级性能优化；关系主线切换。
- **Completed：** 当前有全量树布局和预览原型。
- **Remaining：** 全部目标交互与性能能力。
- **Acceptance Criteria：** 千人样本按验收数据集达到约定加载/定位体验，并可按主线投影显示。
- **Validation：** 未开始。
- **Blockers：** 依赖 P0/P1 的关系投影与查询边界。
- **Next Action：** P1 提供稳定查询投影后，定义性能基准和视图状态模型。

## P3 — 传统吊线图

- **Goal：** 提供可配置、可跨页打印的传统吊线图。
- **Scope：** 独立吊线图引擎；竖排、房支与世代布局；跨页续接；可配置模板；打印页面模型。
- **Completed：** 现有左至右布局仅为原型，不是传统吊线图。
- **Remaining：** 全部目标能力。
- **Acceptance Criteria：** 已确认样张可稳定复现，跨页连接、支系和世代排布符合确认规则。
- **Validation：** 未开始。
- **Blockers：** 需要确认地域谱式、模板和复杂关系展示细则。
- **Next Action：** 在实现前向用户收集样张和版式验收规则。

## P4 — 数据交换与谱书制作

- **Goal：** 提供可靠交换、输出和正式 Windows 发布。
- **Scope：** 版本化 JSON、Excel、GEDCOM；PDF、图片及打印；谱书编纂；Windows 正式发布。
- **Completed：** 当前存在 Excel/GEDCOM/JSON/PDF/XPS 原型入口，尚未作为正式能力验收。
- **Remaining：** 版本化格式、预检/损失报告、稳定导出与发布流程。
- **Acceptance Criteria：** 内部 JSON 可无损往返；外部格式报告映射损失；PDF/图片/打印与预览一致；干净 Windows 环境可离线安装运行。
- **Validation：** 未开始。
- **Blockers：** 依赖 P0 数据模型、迁移、备份和统一页面模型。
- **Next Action：** P0/P3 的数据与页面模型稳定后，优先定义版本化 JSON 契约。
