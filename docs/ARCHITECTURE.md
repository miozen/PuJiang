# 架构契约

## 产品与技术边界

谱匠（PuJiang）的目标是完全离线的 Windows 单机族谱管理系统，不是仅用于排版的工具。技术基线由 [D-001](DECISIONS.md#d-001--产品与技术基线) 约束：.NET 10、WPF、EF Core、SQLite；不依赖云端或服务器。源码目录与命名空间仍为 `ZupuPaiBan`，属于后续专项渐进重命名范围，见 [D-009](DECISIONS.md#d-009--项目名称仓库与分支策略)。

## Current：源码已实现

- `PuJiang.slnx` 统一入口包含一个 `net10.0-windows` WPF 项目和 `PuJiang.Tests` xUnit 项目；`global.json` 固定 SDK `10.0.402`。依赖 CommunityToolkit.Mvvm、EF Core SQLite、ClosedXML、PdfSharpCore。
- 界面位于 `Views/` 与 `MainWindow.xaml`；ViewModel 位于 `ViewModels/`；数据访问位于 `Data/`；布局、打印与交换位于 `Services/`。
- 旧成员树及其操作仍以 `FamilyMember`（可空 `FatherId`、字符串 `SpouseName`、代数、排行）为界面数据模型。WPF 另提供目标模型族谱的创建、会话内选择、按谱人物列表、姓名及基础档案编辑、无关系人物的确认删除，以及生物子女事务新增、已有子女关联、列表和指定生物父母关系的确认解除。有关联关系或婚配记录的人物不可通过此入口删除；解除会删除该关系记录（含备注和来源），但保留人物及其他关系；重新关联建立新关系，不能恢复旧备注和来源。其他人物档案编辑、复杂删除和关系管理尚未切换；这不表示 P1 管理功能已完成。
- 启动路径在现有 SQLite 库有待执行迁移时先使用 SQLite 在线备份生成并校验数据库快照，再调用 `MigrateAsync`；备份失败则终止启动。完整备份包与恢复尚未实现。`Infrastructure/AppDataPaths.cs` 默认使用 `%LOCALAPPDATA%\\PuJiang`，支持显式独立测试数据根目录，详见 [运行规范](OPERATIONS.md#数据与日志目录)。`FileAppLogger` 提供本地轮转日志与未处理异常记录。
- 应用创建主窗口前独占数据目录内的锁文件，并持有至进程退出，阻止支持此协议的其他谱匠实例同时使用该目录；不阻止旧版程序或其他软件直接访问数据库。
- `GenealogyDataService.GetGraphSnapshotAsync` 在读取事务中按单谱提取不可变人物、类型化关系、婚配与房支数据，保留原始记录与来源；它不选择展示主线，也尚未完成大规模性能验收。
- 目标谱系已接入基于快照的关系清单：每个人物作为独立入口，展开显示已记录关系，多条父母关系明确标注未自动选择主线，未知上系不推断为始祖；选择人物节点按 ID 接入现有目标人物档案和生物子女选择，右键可新增生物子女，双击可编辑姓名和基础档案，后者通过按谱及人物 ID 限定的单条更新原子写入。关系详情只读；这不是世代层级树，尚不支持旧版工具栏操作。相关变更后会刷新清单。
- `ZupuDbContextFactory` 提供不执行创建或迁移的 EF Core 设计时上下文；`dotnet-tools.json` 固定本地 `dotnet-ef` 10.0.12。
- `PuJiang.Tests` 使用每测试独立临时 SQLite 文件（禁用连接池后清理）；当前覆盖迁移、跨谱隔离/拒绝、父系循环、失考、重复关系、批量关系写入的提交/回滚（包含同批次及已有关系混合循环）、布局确定性与虚构千人样本。测试分类为 Unit、Integration、Layout、Performance；尚无 UI 自动化。
- 有基础成员树、横向/左至右树布局、预览、打印、PDF/XPS 及 Excel/GEDCOM/JSON 入口。它们是原型能力，不代表正式数据交换、排版或大规模性能已验收。

旧成员树模型不支持多族谱隔离、母系、正式配偶关系、多次婚配、过继/嗣继、房支、失考或关系主线选择。详见 [D-002](DECISIONS.md#d-002--多族谱隔离) 至 [D-007](DECISIONS.md#d-007--重建范围与架构尺度)。

## Target：已确认目标架构

### 模块职责

保持一个 Windows 桌面应用，按职责形成清晰边界，而非拆分为微服务：

- **Presentation**：WPF Views/ViewModels，只负责交互、状态和导航。
- **Application**：用例编排、事务边界、导入预检、校验调用和 DTO/视图投影。
- **Domain**：人物、关系、谱系规则、世代/排行推导与校验，不依赖 WPF。
- **Infrastructure**：EF Core/SQLite、文件系统、备份、JSON/Excel/GEDCOM、打印/PDF。
- **Rendering**：从只读谱系投影生成布局和页面模型；世系图、吊线图与输出渲染器各自独立。

初期可仍在单一项目内以目录和接口边界落地；仅在编译、测试或发布收益明确时再拆工程。

### Target：数据与关系边界

- `Genealogy` 是稳定根实体。任何族谱内人物、关系、房支、导入批次、设置和备份元数据都以 `GenealogyId` 隔离。
- `Person` 保存人物档案及资料完整度；未知或信息不完整的历史记录合法。
- 父母、子女、婚配、收养、嗣继、失考等以类型化关系/家庭记录表达，保留生物关系与法理/谱载关系及来源。不得再用配偶姓名字符串承载关系。
- 房支独立于人物与关系；世代和排行分别保存谱载原始值、推导参考值及来源/确认状态。展示主线是查询投影规则，不能改写原始关系。
- 数据库层负责外键、跨谱保护、必要唯一性；领域层负责循环、关系冲突、世代一致性和业务校验。

### Target：图、排版与输出边界

- **交互式世系图**：面对查询投影，支持逐代展开/折叠、搜索定位、路径高亮和大规模按需渲染。
- **人物管理入口**：按 [D-011](DECISIONS.md#d-011--成员树优先的操作体验) 逐步接入成员树的右键、双击和工具栏；现有目标模型列表为过渡验证界面。
- **传统吊线图**：独立布局引擎、独立页面切分；其地域谱式和复杂关系图例仍为 Proposed，见 [D-008](DECISIONS.md#d-008--传统吊线图细则)。
- **打印/PDF/图片**：共享“布局结果 → 页面模型”的中间层；各输出器只负责渲染，不能各自重新解释谱系关系。

### Target：持久化、安全与演进

- 使用 EF Core Migration 管理模式升级；应用启动不得以 `EnsureCreated` 承担生产升级。
- 所有导入、复杂关系变更和恢复在明确事务边界内执行；先预检，再应用，并产生可读报告。
- 备份是版本化离线包；恢复应在独立副本验证后替换目标，保留失败诊断。
- 自动化测试优先覆盖迁移、关系校验、导入事务、备份恢复与布局数据投影。

## Proposed：待确认而非既定设计

- 吊线图地域流派、页面样式模板、跨页续接规则和复杂关系可视化。
- 具体的照片/附件存储、备份包加密、发布安装器实现。
- 人物标识、来源引用和房支编码的最终字段规范。

这些方案在获得确认前不得写成已完成或既定架构。

## Windows 构建、测试与发布

- 在 Windows 原生环境使用 .NET 10 SDK 执行 `dotnet restore PuJiang.slnx`、`dotnet build PuJiang.slnx --no-restore --configuration Release`，以及日常测试 `dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --filter "Category!=Performance"`。
- GitHub Actions 位于 `.github/workflows/ci.yml`，使用 `windows-latest` 和相同入口；远端尚未验证前不得宣称 CI 已通过。
- 自动化测试以测试专用临时 SQLite 路径运行；不得启动会写入真实用户文档目录数据库的应用验证。
- 正式发布目标是 `win-x64` self-contained；安装包、签名和自动更新仍待 P4。正式发布前需验证干净 Windows 环境的安装、首次迁移、离线运行和打印/PDF 输出。
