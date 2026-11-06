# 接力状态

- **WB-B-05 验收（2026-11-06）：** 空谱或有谱画布右键按位置预填世代，工具栏/快捷键初始一世但右侧可改；原位姓名 Enter/空白确认、Esc 取消，默认男性，确认后即时选中显示，显式保存且不创建父母。空谱输入期间监测投影失效；不改数据库结构。Release 构建通过（0 错误、17 警告），定向 4/4、WPF 专项 24/24、日常回归 460/460 通过，均无失败或跳过。用户对隔离测试数据 `artifacts/wb-b05-review/data` 的空谱首节点体验反馈“符合预期”，列明范围已验收；不推断其他切片或阶段通过。`docs/references/` 用户素材保留本机。

- **WB-B-04 验收（2026-11-06）：** 已接入普通兄妹原位新增，继承零、一或两位明确生父母，保留原人物关系并独立保留兄妹关系；未知或重复生父母角色阻止并提示，养父母/继嗣不自动继承。Enter/空白确认、Esc 取消，显式事务保存；不修改数据结构。Release 构建通过（0 错误、25 警告），定向 11/11、WPF 专项 23/23、日常回归 459/459 通过，均无失败或跳过。用户对隔离虚构库 `artifacts/wb-b04-review/data` 的新增兄妹体验反馈“符合预期”，列明范围已验收；不据此推断其他切片或整个阶段通过。历史多父母主线仍须复核，性别图标外观仍待后续优化。

- **WB-B-03 复验过程（2026-11-06，已完成）：** 用户未接受配偶滚动隐藏，要求按 `docs/references/多配偶.png` 将配偶以姓名框和连线逐个显示，并在姓名前显示性别图标；确认原性别默认规则不变。修正版已在独立虚构库复验，用户确认“可以，就这样”；图标与参考图不符，用户同意留待后续视觉优化。修正版 Release 构建通过（25 条警告），定向测试 10/10、WPF 专项 23/23 通过；日常回归 453/453 通过；补充多配偶并存子女连线的测试后，重新构建和 WPF 专项 23/23 通过，最终日常回归 453/453 通过。多配偶并存子女连线已由专项测试覆盖；不在画布主体的配偶入口仍受原窗口限制。`docs/references/` 所有素材均保留本机。

- **WB-B-03 首次验收前记录（2026-11-06，已由上述复验取代）：** 画布主体人物可在配偶区域原位输入，Enter/空白确认、Esc 取消；已知性别默认相反，未知保持未定，确认后选中新配偶，显式保存时人物与婚配同事务提交并保持选中。已有婚配和子女关系不重算；不在画布主体的配偶入口仍受原窗口限制。Windows Release 解决方案构建通过（0 错误、16 条警告），定向 10/10、WPF 专项 23/23、日常回归 453/453 通过，均无失败或跳过；用户验收尚在进行。独立虚构库为 `artifacts/wb-b03-review/data`；`docs/references/` 是用户本机素材，保留且不加入提交。未修改数据库结构，未开始下一切片。


- **WB-B-02 验收（2026-11-06）：** 普通子女从男女上下文原位录入，Enter 或单击空白确认、Esc 取消；默认配偶按已确认的稳定婚配 ID 次序，人物与生物父母同事务保存。读取已保存人物时显示其已记录父母；新建时即时连线，同会话子女的未确认顺序标注为临时显示、非谱载排行。用户反馈空白确认、父母显示与连线“现在都符合预期”；参考用户提供的本机样图（未随 Git 同步） 调整为紧凑姓名色块、选中后框外显示资料，用户反馈“可以了，先这样吧”。重开或外部变更后临时主线选择失效，已有多父母仍提示复核，不自动推断。最终源码 Release 构建成功（0 错误、25 警告），工作台 WPF 专项 19/19、日常回归 449/449 通过，均无失败或跳过；`git diff --check` 通过。用户已确认列明范围，提交推送状态以 Git 实测为准。独立虚构库为 `artifacts/wb-b02-review/data`。


- **WB-B-01 最终验收（2026-11-06）：** 用户反馈 Enter 后画布应立即显示草稿姓名，其余体验“没有什么问题”；已修复节点优先显示当前人物草稿，保持显式保存边界，用户对修正版反馈“通过了”。这只覆盖本切片有限范围，不代表四类新增或 P2 已完成；新增位置投影接口由自动化验证，尚未接入产品新增入口。Windows `dotnet build PuJiang.slnx --no-restore --configuration Release --verbosity quiet` 通过（0 错误、24 警告）；同源码日常测试 445/445、WPF 专项 16/16、定向测试 6/6 通过，均无失败或跳过。首次构建被本轮隔离实例占用而失败，用户关闭后重试通过；Performance 与远端 CI 未执行。数据目录为 `artifacts/wb-b01-review/data`，独立虚构库。切片完成后按用户授权检查并提交推送，实际 Git 状态以命令实测为准。


- **WB-B-01 本轮（2026-11-06）：** 新增 `InlineNamePlacement` 复用横向布局投影临时节点，不创建人物/关系；工作台输入中阻止切人、切谱、保存、退出和其他画布快捷键，重绘保留输入与位置，数据变化后保留未确认文本并拒绝应用。`BeginProjectedNameInput` 是后续新增入口可调用的底座，目前没有产品新增入口接入；现有节点原位改名仍先进入草稿。Windows Release 解决方案构建成功（0 错误、24 警告）；同源码日常测试 445/445、工作台与姓名控件 WPF 专项 16/16 通过，均无失败或跳过；`git diff --check` 通过。测试库 `artifacts/wb-b01-review/data` 是独立虚构数据，启动器位于 `artifacts/wb-b01-review/launcher/bin/Release/net10.0-windows/Launcher.exe`。助手通过 computer-use 识别隔离主窗口，但画面捕获及重试分别超时，未执行人工视觉交互；用户人工验收待完成。未实施 B-02、数据库结构或迁移。暂不提交推送。


- **2026-11-06 本轮：** 用户确认普通新增子女及兄妹规则并授权 D-026/D-028 按新规则执行，已记录决策及修订切片。InlineNameEditor 复用姓名校验，试用画布对已有节点提供原位编辑姓名，Enter 更新草稿、Esc 取消；尚非四类新增，未完成 WB-B-01。Windows Release 解决方案构建成功（0 错误、24 警告）；同源码定向 UI 测试 2/2、工作台 UI 专项 13/13 和日常回归 443/443 通过，均无失败或跳过。未进行用户人工验收或 Performance；沙箱内 GitHub HTTPS TCP 443 连接失败；沙箱外只读 ls-remote 成功，远端 dev 与本地 HEAD 均为 0154ce4；CI 未查询。不修改数据库结构。


- **V1 阶段 A（2026-11-06）：** 定向审查现有工作台、关系模型、草稿事务和 Accepted 契约，制定 B–E 切片及验收；无业务代码或数据库修改。母亲语义及特殊关系待用户确认，详情见 [V1 清单](WORKBENCH_V1_SLICES.md)。本轮仅文档检查，不运行构建、测试、UI 或人工验收；远端实时查询网络失败，CI 未查询。以下为历史记录，实际 Git 状态以命令实测为准。

- **Last Updated：** 2026-11-06（Asia/Shanghai）
- **Current Branch：** `dev`，跟踪 `origin/dev`。
- **HEAD / Last Commit：** 以 `git log -1 --oneline` 及 `git rev-parse HEAD` 实测为准；不得以本文件中的历史 SHA 判断同步状态。
- **Working Tree：** 以 `git status --short --branch` 为准。
- **Remote Sync：** 以本机 Git 状态及实际远端核对结果为准。
- **Current Phase：** P2 — 交互式世系图。
- **Current Task：** WB-B-05 列明范围已验收；原 P2-3 持久排序仍未接 UI，D-014 不变。
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
- **本轮持久排序规划：** 用户确认每张图表分别保存；新增/删除同胞或父母关系变化时保留旧顺序、复核前不自动应用。决策见 [D-028](DECISIONS.md#d-028--真实谱系只读横向投影的处理原则)，分步模型、并发、复核及验收见[方案](SIBLING_ORDER_PERSISTENCE_DESIGN.md)。这是规划，不表示持久存储、迁移或界面已实现。
- **本轮规划验证：** 纯文档修改已执行 `git diff --check` 和相关文档本地相对链接存在性检查，均通过；未修改应用源码，未运行构建、测试或远端 CI，也未访问数据库。
- **本轮持久排序存储：** 新增迁移、按图表的排序组及历史项、受保护独立会话的完整子女组捕获、事务保存和差异复核；图表修订条件更新，旧图表选线保存不覆盖排序，旧人物/关系身份保留供复核。成功保存后必须重新打开会话；数据或图表变化使旧结果失效。全库代数变化即使无关也保守要求复核；超过 2000、缺失世代或多父母拒绝捕获。此切片未接 UI、无父母兄妹或复杂关系。
- **本轮存储本地验证：** Windows `dotnet build PuJiang.slnx --configuration Release --no-restore --verbosity quiet` 成功（0 错误、32 警告）；同源码日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --no-build --filter "Category!=Performance&Category!=UI" --verbosity quiet` 443/443 通过；单独筛选 `FullyQualifiedName~SiblingOrderStorageTests|FullyQualifiedName~LineageChartTests` 为 15/15 通过，均无跳过。覆盖旧库迁移、图表与谱隔离、并发竞争、取消与 SQLite 注入回滚、历史/ABA 复核、超过 100 人、图表选线保留、关闭排空及备份预检；EF `migrations has-pending-model-changes` 检查无差异。最初 EF 工具构建因 NuGet 网络不可用失败，使用本机缓存 `dotnet restore --ignore-failed-sources` 后恢复成功。未运行 UI、Performance 或人工验收，未查询远端 CI；未使用真实数据。
- **本轮清单整理：** 用户要求暂停后续开发，并希望按序号查看 P2-3 已完成与剩余工作；已在[计划](CURRENT_PLAN.md#p2-3-有序切片清单)建立 14 项清单，6 项为所列有限范围已完成、8 项未完成，项数不等于工期比例。本轮仅改文档，`git diff --check`、相对链接和 14 项计数检查通过；未运行构建、应用测试或远端 CI。
- **Exact Next Action：** 核对 WB-B-06 选中与联动前置条件，按单切片流程实施和验收。
- **Cross-machine Ready：** 以实际 Git 状态和远端 CI 核实；未提交或未推送的内容不能视为远端接力完成。

## 下一台电脑接力

- Windows 原生环境按 [跨电脑接力步骤](OPERATIONS.md#跨电脑接力) 获取 `dev`、检查工作区和远端、还原并验证；SDK 版本约束以 `global.json` 为准。
- 仓库内恢复替换协调器和阶段记录为测试原型；应用已接入共享数据库操作排空与正常退出协调，但未覆盖所有异步工作及外部访问，不能将其视为可安全恢复。数据库专用备份、用户数据及人工验收边界见 [架构](ARCHITECTURE.md) 和 [运行规范](OPERATIONS.md)。
- 上述测试和验收结果是当前电脑历史结果，新电脑须分别执行并报告；人工 UI 只能在独立测试数据目录进行。Git 不会自动带走仓库外的用户数据库、附件、配置或本机测试目录；暂存前仍须核对忽略规则。
- 可直接交给本机后续 Codex：`先阅读最新 HANDOFF、V1 切片及相关契约；核对 WB-B-06 前置条件并按单切片流程实施验收，保留 P2-3 排序待办并遵守 D-014。`
