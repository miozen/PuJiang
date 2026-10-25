# 架构契约

## 产品与技术边界

谱匠（PuJiang）的目标是完全离线的 Windows 单机族谱管理系统，不是仅用于排版的工具。技术基线由 [D-001](DECISIONS.md#d-001--产品与技术基线) 约束：.NET 10、WPF、EF Core、SQLite；不依赖云端或服务器。源码目录与命名空间仍为 `ZupuPaiBan`，属于后续专项渐进重命名范围，见 [D-009](DECISIONS.md#d-009--项目名称仓库与分支策略)。

## Current：源码已实现

- `PuJiang.slnx` 统一入口包含一个 `net10.0-windows` WPF 项目和 `PuJiang.Tests` xUnit 项目；`global.json` 固定 SDK `10.0.402`。依赖 CommunityToolkit.Mvvm、EF Core SQLite、ClosedXML、PdfSharpCore。
- 界面位于 `Views/` 与 `MainWindow.xaml`；ViewModel 位于 `ViewModels/`；数据访问位于 `Data/`；布局、打印与交换位于 `Services/`。
- 旧 WPF 原型仍以 `FamilyMember`（可空 `FatherId`、字符串 `SpouseName`、代数、排行）为界面数据模型。P0 源码另有未接入 UI 的 `Genealogy`、`Person`、`PersonRelationship`、`FamilyUnion`、`FamilyBranch` 目标实体、首个 Migration 与 `ZupuDbContextModelSnapshot`；这不表示 P1 管理功能已完成。
- 启动路径已使用 `MigrateAsync`；`Infrastructure/AppDataPaths.cs` 规定 `%LOCALAPPDATA%\\PuJiang` 数据根目录，`FileAppLogger` 提供本地轮转日志与未处理异常记录。备份恢复尚未实现。
- `ZupuDbContextFactory` 提供不执行创建或迁移的 EF Core 设计时上下文；`dotnet-tools.json` 固定本地 `dotnet-ef` 10.0.12。
- `PuJiang.Tests` 使用每测试独立临时 SQLite 文件（禁用连接池后清理）；当前覆盖迁移、跨谱隔离/拒绝、父系循环、失考、重复关系、批量关系写入的提交/回滚、布局确定性与虚构千人样本。测试分类为 Unit、Integration、Layout、Performance；尚无 UI 自动化。
- 有基础成员树、横向/左至右树布局、预览、打印、PDF/XPS 及 Excel/GEDCOM/JSON 入口。它们是原型能力，不代表正式数据交换、排版或大规模性能已验收。

当前模型不支持多族谱隔离、母系、正式配偶关系、多次婚配、过继/嗣继、房支、失考或关系主线选择。详见 [D-002](DECISIONS.md#d-002--多族谱隔离) 至 [D-007](DECISIONS.md#d-007--重建范围与架构尺度)。

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
