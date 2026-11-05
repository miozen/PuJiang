# 接力状态

- **Last Updated：** 2026-11-05（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** P2-3 同胞排序：右侧逐人指定会话顺序和右键上下移动已实现并获用户体验反馈；持久化排序和复杂关系仍为后续切片，D-014 不变。
- **Last Completed：** 依 [D-028](DECISIONS.md#d-028--真实谱系只读横向投影的处理原则) 用户补充，缺世代首次说明经确认后在当前窗口自动标注推导；路径说明和确认合并，拒绝保留原图。此前补测多父母、失效图表及跨页候选，详见[横向设计](HORIZONTAL_LINEAGE_DESIGN.md)。
- **Uncommitted Work：** 以本机 `git status --short` 为准；另一台电脑曾有的 `App.xaml` 行尾差异并未出现在本机。
- **Validation Performed：** Windows 原生 `dotnet build PuJiang.slnx --no-restore --configuration Release --verbosity quiet` 成功（0 错误、24 警告）；相同源码日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --filter "Category!=Performance&Category!=UI" --verbosity quiet` 396/396 通过；`Category=UI&DisplayName~real-horizontal-path` WPF 专项 6/6、`Category=UI&DisplayName~real-horizontal-rules` 1/1 通过，均无失败或跳过。指定隔离 WPF 场景确认拒绝保留原图、首次确认合并、后续不重复请求推导、原始谱载不变；路径读取隔离专项此前 4/4 通过。`artifacts/p2-path-review-20261104/data` 为虚构库，用户此前对多父母选择框、路径确认、四代高亮和诊断反馈“符合预期”；本轮对合并后的缺世代提示反馈“看清楚了”，助手观察确认后节点标注“推导”。助手另在该窗口选择待复核图表，观察历史选线及原因保留且未补载，先前在合并提示的前一版本点击“否”观察原图保留；这些助手观察不等同用户对失效及取消的验收。未访问正式用户数据。
- **Validation Pending：** 本轮远端 CI 未查询；横向大谱 Performance、直接画布编修用户验收及复杂关系完整矩阵尚未完成，视口虚拟化尚未实现。常见世代文字识别已获用户确认，其他跨支规则仍待明确；D-014 关闭项不变。
- **Known Issues / Blockers：** 失效图表路径结果的底部提示较笼统，详细原因与身份混有长 ID，需改善可读性；全库代数造成无关变更也需重新确认；不能防护第三方有意篡改代数或替换数据库；未来重建受保护表的 Migration 必须显式重建触发器；SQLite 快照仅含数据库；ImageSharp 1.0.4 存在已知高、中危告警。
- **本机用户验收：** `artifacts/p2-acceptance-20261104/data` 为独立测试目录，生成 11 位虚构人物、四代、三个房支及失考、未详婚配和方向配偶记录；以 `PUJIANG_TEST_DATA_ROOT` 启动真实只读窗口。助手逐支加入始祖、三房、两房子女及长房四世后代，观察首子水平对齐、子树占位及正交连线。用户对所请求的世代列、同胞顺序、长房及次房折叠/展开、占位、连线和诊断反馈“通过”；不推断其单独操作了顺序调整或其他未展示场景。该目录及临时种子工程均被 Git 忽略，未使用正式用户数据。种子工程构建成功，出现既有 ImageSharp 安全警告及 NuGet 漏洞信息源不可用警告；本轮未运行测试。
- **本轮接力：** 用户要求整理并推送原型检查点；实际 Git 状态与远端同步以本机核对为准，测试数据目录不随 Git 同步。
- **本次验收进度：** 用户反馈旧的逐项勾选操作过于复杂，选择首次说明后临时推导并标记，并确认新版说明看清楚了；已记录决定与实现。失效图表及取消流程的用户视觉验收尚未进行，不可将自动测试或助手观察当作用户反馈。
- **接力检查点：** 新顺序和 P2-1/P2-2/P2-3 映射见[计划](CURRENT_PLAN.md#p2--交互式世系图)，安全边界见 [D-026](DECISIONS.md#d-026--专业族谱编修工作台与显式草稿)及[工作台设计](WORKBENCH_DESIGN.md)。用户已授权将此前保留的源码、测试和文档一并提交推送；本次同步未运行构建或测试，历史验证见上文，未访问数据库。实际远端及 CI 仍须以查询为准。
- **本轮原型：** 主窗口“视图 → 世系编修工作台（内存原型）”打开独立窗口；内置虚构谱、空白谱，世代列、展开折叠、独立人物、跨代过继与引用、搜索、选中、缩放和平移。右侧编辑与新增入口暂不可用；不接入数据库或持久化。仅为固定样本布局，复杂关系路由、正式业务与性能尚未实现。Windows Release 构建成功（0 错误，已有依赖警告）；日常测试 396/396、单独原型 UI 检查 1/1 通过；用户确认范围见本轮反馈，远端 CI 尚未确认。
- **用户反馈（2026-11-05）：** 独立测试数据目录启动真实窗口，用户查看后表示“目前像是我期望的样子去了……暂且先这样”；这是整体方向反馈，不推断已逐项操作或确认完整验收。
- **本轮反馈：** 用户反馈左侧谱系管理框宽度合适、需要加高，其余体验部分合适；调整后用户确认“现在先通过了”。未来按钮完善后的剩余空间布局要求见 [D-026](DECISIONS.md#d-026--专业族谱编修工作台与显式草稿)。未逐项记录用户操作，不推断完整 P2 或大谱性能通过。
- **本轮验证：** Windows Release 解决方案构建成功（0 错误、16 条既有依赖警告）；同源码日常测试 396/396、原型 UI 测试 1/1 通过，均无失败或跳过。首次构建曾因测试实例占用可执行文件失败，关闭本轮隔离实例后重试通过；远端 CI 未验证。
- **P2-3 基础验证：** Windows `dotnet build PuJiang.slnx --no-restore --configuration Release --verbosity quiet` 成功（0 错误、31 警告）；同源码日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --filter "Category!=Performance&Category!=UI" --verbosity quiet` 398/398 通过、无跳过。新增两项隔离 SQLite 测试覆盖校验、同谱、ABA 版本冲突、注入失败和草稿保留；尚未覆盖工作台切换/关闭、提交后刷新失败、操作排空或人工验收。当前未接入产品界面。
- **P2-3 会话验证：** 本轮同命令 Release 构建成功（0 错误、31 警告），日常测试 400/400 通过、无跳过；新增隔离测试覆盖服务层切谱/离开的保存、放弃、取消、校验失败、提交后刷新失败和在途操作排空。真实 WPF 窗口、主窗口退出顺序与人工验收尚未覆盖。用户要求今后不等待远端 CI；本轮未查询远端结果。
- **P2-3 试用窗口验证：** 同源码 Windows Release 解决方案构建通过（0 错误、31 警告），日常测试 401/401 通过；显式 UI 专项 3/3 通过（两项试用窗口及一项既有横向窗口退出），无跳过。独立 SQLite 与实际 WPF 窗口覆盖切人取消/放弃、切谱取消、关闭取消、主窗口真实关闭事件取消后再保存退出、提交后通知刷新失败及旧令牌不重试。尚未人工验收，也未验证横向画布编辑或大谱性能；不使用正式用户数据，未等待远端 CI。
- **本机人工验收准备：** 在 `artifacts/p2-profile-acceptance-20261105/data` 新建两套虚构谱和四位虚构人物，以 `PUJIANG_TEST_DATA_ROOT` 启动隔离实例；测试目录及种子工程被 Git 忽略。本机 Release 构建成功（0 错误、39 警告；首次依赖还原受网络限制，重试成功），本轮未运行测试。窗口截图接口两次超时，已请用户自行打开“视图 → 世系编修工作台（人物档案试用）”并反馈；尚无用户验收结果。横向只读会话会在数据库写入后失效，后续接入须重建会话并安全恢复视图状态，不能沿用旧结果。
- **本机人工验收反馈：** 用户对请求的档案试用体验反馈“通过了，没有发现什么问题”。这是该试用窗口的人工反馈，不推断目标横向画布或 P2 完整验收通过。
- **横向会话基础验证：** 新增可重建的单谱只读会话封装，尚未连接工作台。Windows Release 解决方案构建成功（0 错误、15 警告）；同源码日常测试 404/404 通过、无跳过，其中三项隔离 SQLite 测试覆盖保存后旧版本失效与重开、取消重建、并发重建时在途旧结果拒绝和会话排空。本轮未运行 UI 或性能测试，未等待远端 CI。
- **单人物画布验证：** Windows `dotnet build PuJiang.slnx --no-restore --configuration Release --verbosity quiet` 成功（0 错误、16 警告）；同源码日常测试 405/405、显式 WPF 专项 5/5 通过，均无跳过。独立 SQLite 与实际 WPF 覆盖明确世代列、画布选人取消、切谱、主窗口退出、保存后重开并恢复节点、刷新失败及谱载世代变化时拒绝恢复、外部变化清空节点并保留草稿；单人物读取在途被重建取代时拒绝返回。未进行用户人工验收、性能测试或完整多节点图验收，不使用正式数据，未等待远端 CI。
- **用户反馈（2026-11-05）：** 用户否定单人物额外确认世代的流程，希望像 AutoCAD 或 Photoshop 一样在画布右键编辑、新建人物，确定首节点后直接增加子女、配偶和资料；允许识别常见世代写法，无法识别或冲突时保留原文。旧单人物画布体验未通过，本次调整尚待用户验收。
- **画布编修检查点：** 新试用窗口可在空白处右键或 Ctrl+N 新建、人物右键编辑或新增生物/收养子女及配偶；草稿保存时人物和新增关系同事务提交，失败保留草稿，保存后重建画布并拒绝旧结果。仅前 100 人参与投影，未知世代、排序歧义、多父母和跨页关系保留诊断，复杂关系与同胞排序未完成；配偶附于人物卡片。独立测试数据和临时种子不进入 Git，不使用正式用户数据。
- **画布编修本地验证：** Windows `dotnet build PuJiang.slnx --configuration Release --no-restore --verbosity quiet` 成功（0 错误、16 警告）；同源码日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --filter "Category!=Performance&Category!=UI" --verbosity quiet` 429/429，通过、无跳过。显式 WPF 专项（`Category=UI&FullyQualifiedName~WorkbenchProfileWindowTests|Category=UI&DisplayName~real-horizontal-exit`）9/9 通过、无跳过；覆盖真实窗口右键新建、子女及配偶、切换、退出和刷新失败，事务失败回滚与旧结果失效由隔离测试覆盖。性能未测试；依赖 ImageSharp 已知漏洞告警仍在，远端 CI 未查询。
- **用户反馈（2026-11-05，本次）：** 新增人物、新增子女和配偶“基本上符合预期”，指出遗漏新增兄妹；要求 P2-3 分小功能切片推进。同胞排序期望右键上移、下移，右侧设置排第几；无父母可直接保留兄妹关系，多个父母候选由用户选择共同父母及关系类型。此反馈不等于完整 P2-3 验收。
- **兄妹体验反馈：** 使用 `artifacts/p2-direct-acceptance-20261105/data` 独立虚构测试库，用户对要求体验的右键新增兄妹反馈“符合预期”。不推断用户分别操作了全部无父母及多父母分支；后者由自动化覆盖。
- **兄妹切片本地验证：** Windows `dotnet build PuJiang.slnx --configuration Release --no-restore --verbosity quiet` 通过（0 错误、16 警告）；同源码日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --filter "Category!=Performance&Category!=UI" --verbosity quiet` 430/430 通过；WPF 专项 `Category=UI&FullyQualifiedName~WorkbenchProfileWindowTests|Category=UI&DisplayName~real-horizontal-exit` 11/11 通过，均无跳过。验证无父母创建、多父母选择、世代预填、跨谱拒绝、版本守卫及事务故障回滚；未测性能，未查询远端 CI。旧版对新增关系类型及交换文件的兼容尚未验收。
- **同胞排序本地验证：** Windows `dotnet build PuJiang.slnx --configuration Release --no-restore --verbosity quiet` 通过（0 错误、8 警告）；同源码日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --filter "Category!=Performance&Category!=UI" --verbosity quiet` 430/430，通过；显式 WPF 专项 `Category=UI&FullyQualifiedName~WorkbenchProfileWindowTests|Category=UI&DisplayName~real-horizontal-exit` 13/13 通过；均无跳过。覆盖完整与无父母同胞组、重复位置拒绝、右键移动、不写谱载原文和外部失效。初次构建出现局部变量未赋值编译错误并已修复，初次专项有旧诊断断言失败，恢复诊断后重跑通过。尚未测性能，未查询远端 CI。
- **同胞排序人工验收：** `artifacts/p2-sibling-order-20261105/data` 为独立虚构库，含一位父母及三名世代明确的虚构子女；用户对右侧指定位置和右键上下移动的请求反馈“符合预期”，并选择“先按会话排序验收”。不推断持久排序、复杂关系或完整 P2 已通过；该目录和启动器不进入 Git。
- **Exact Next Action：** 按用户的小切片节奏，设计并实现同胞排序的持久保存及失效复核方案，先核对 D-028 并在需要时确认数据语义；保留谱载原文，使用独立数据验证，遵守 D-014。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`按最新 HANDOFF，以独立小切片规划 P2-3 同胞排序持久保存和失效复核；遵守 D-014，本地验证后不等待远端 CI。`
