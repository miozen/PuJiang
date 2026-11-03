# 开发、数据安全与发布规范

## 本地验证与远端 CI

采用本地验证优先、远端 CI 独立验收。代码修改的最终本地验证在 Windows 原生环境使用满足 `global.json` 的 .NET SDK，在仓库根目录按当前 [.github/workflows/ci.yml](../.github/workflows/ci.yml) 执行与 CI 等效的 Release 解决方案构建和日常测试：

```powershell
dotnet restore PuJiang.slnx
dotnet build PuJiang.slnx --no-restore --configuration Release
dotnet test PuJiang.Tests/PuJiang.Tests.csproj --no-build --configuration Release --filter "Category!=Performance&Category!=UI" --logger "trx;LogFileName=test-results.trx" --results-directory TestResults
```

首次使用、新电脑、还原产物缺失或失效，以及依赖、项目文件、目标框架、SDK、NuGet 源或影响还原的配置发生变化时，先重新执行 `dotnet restore`；仅在已有有效还原产物与当前配置相符时可跳过重复还原。`--no-build` 仅用于当前源码和相同 Release 配置已成功完成解决方案构建之后，不得复用陈旧构建。`dotnet tool restore` 仅在需要仓库本地工具时执行；`dotnet publish` 属于发布专项，不是每轮日常验证。

本地构建或测试失败时先定位并修复，再重新运行受影响验证；若环境或其他阻塞使验证无法完成，记录实际失败与未验证范围，不得宣称任务完成。纯文档或其他不影响程序的修改按实际范围检查文档和 Git diff，不要求应用构建测试。

本地验证通过后，可依据既有 Git 授权提交并继续工作，无需每轮等待远端。GitHub Actions 继续独立运行；阶段验收、PR 合并和重要里程碑必须确认对应提交的远端 CI 已通过。远端失败须优先定位环境差异或真实回归，不得删除测试、放宽断言或忽略失败以获得通过。对于纯文档变更，工作流可能按路径过滤跳过，应报告为未触发/未验证，不得标为远端通过；若该提交恰是阶段或 PR 验收对象，须先明确并验证该验收适用的代码提交与远端检查，不能把被跳过的检查当作通过。

每次报告分别标明本地通过、远端通过、远端运行中或未验证；发生失败时直接报告失败、原因及受影响提交。`Performance` 和 `UI` 测试须分别使用 `--filter "Category=Performance"` 和 `--filter "Category=UI"` 显式运行；人工 UI 验收另行记录。本地或远端日常测试均不能替代这些专项验收。

## 跨电脑接力

1. 在 Windows 电脑准备 Git、可访问仓库的认证，以及满足仓库 `global.json` 的 .NET 10 SDK。当前要求 `10.0.402`，允许同一 feature band 的最新补丁（本机已验证 `10.0.403`）；不要求预先安装 EF 全局工具。依赖还原需要网络及相应 NuGet 源。
2. 新电脑尚无仓库时，在选定的空目录中获取 `dev`：

   ```powershell
   git clone --branch dev https://github.com/miozen/PuJiang.git
   cd PuJiang
   ```

3. 已有仓库时先执行 `git status --short --branch`、`git remote -v` 和 `git log -1 --oneline`。如有未提交内容或本地独有提交，先保留并核对，不要用 `reset`、`clean` 或强推覆盖。工作区安全且用户已授权切换时，切到 `dev` 并先获取远端，再只允许快进：

   ```powershell
   git fetch origin
   git switch dev
   git pull --ff-only origin dev
   ```

4. 验证实际同步：`git status --short --branch`、`git rev-parse HEAD`、`git rev-parse origin/dev`；必要时用 `git ls-remote origin refs/heads/dev` 核对实际远端。如果获取失败、本地有独有提交或哈希不一致，先报告差异；本地 `origin/dev` 是上次获取的引用，不等于实时远端。
5. 按 [AI 开发协议](../AGENTS.md) 依次阅读 [接力状态](HANDOFF.md)、[当前计划](CURRENT_PLAN.md)、与任务有关的 [架构契约](ARCHITECTURE.md) 及 [决策契约](DECISIONS.md)。检查源码、测试与接力是否一致，再执行 `HANDOFF.md` 的 **Exact Next Action**；当前状态以这些文件和 Git 核实结果为准。
6. 按[本地验证与远端 CI](#本地验证与远端-ci)在新电脑还原、构建和测试；UI、Performance 与人工 UI 验收分别记录。迁移工具检查需要时再执行 `dotnet tool restore`；Windows 日常测试中的进程级用例需要 `dotnet` 和 Windows PowerShell 可执行文件。
7. Git 只同步已提交的文件；`.gitignore` 排除 `artifacts/`、常见数据库、日志和构建产物，但并不保证忽略所有附件或任意位置的测试数据，暂存前必须检查。正式用户数据位于仓库外，新电脑不会通过源码同步自动获得本机数据库、附件或配置。真实数据的迁移和恢复须单独安排；当前数据库专用备份包不含附件、配置，且正式恢复尚未实现。开发启动必须使用新的独立测试目录，详见下节。

每次检查点分别记录提交、推送及实际远端验证状态；远端运行中不必阻塞日常后续工作，阶段验收、PR 合并和重要里程碑仍须满足上述远端通过门槛。同步按 [AI 开发协议](../AGENTS.md#handoff-与-git-安全) 和用户的当前指令执行。

迁移工具固定在仓库的 `dotnet-tools.json`。在 Release 构建后，可用下列无连接检查验证当前模型与快照一致，且不会创建或访问用户数据库：

```powershell
dotnet tool run dotnet-ef migrations has-pending-model-changes --project ZupuPaiBan/ZupuPaiBan.csproj --startup-project ZupuPaiBan/ZupuPaiBan.csproj --context ZupuDbContext --configuration Release --no-build
```

## 数据与日志目录

正式用户数据根目录为 `%LOCALAPPDATA%\PuJiang`，程序安装目录不得保存可变用户数据。约定子目录为：`pujiang.db`、`Attachments`、`Backups`、`Exports`、`Logs`。照片和文献附件应放入 `Attachments`；数据库、附件、配置和格式版本是未来备份恢复范围。

人工测试可在启动进程前设置 `PUJIANG_TEST_DATA_ROOT` 为专用测试目录的绝对路径；该进程的数据库、设置、日志及附件目录都会使用该根目录。未设置时保持正式路径；无效、相对或与正式目录重叠的路径会报错，不会回退到正式目录。使用新建的专用目录，不要指向符号链接或真实用户数据。例如在独立 PowerShell 进程中：

```powershell
$env:PUJIANG_TEST_DATA_ROOT = 'D:\20_Projects\PuJiang\artifacts\ui-test-data'
dotnet run --project ZupuPaiBan/ZupuPaiBan.csproj --configuration Release --no-build
```

测试完成后退出该进程；环境变量只对该进程及其子进程生效。测试目录中的数据不会自动删除。

日志只记录启动、未处理异常和必要技术上下文，不记录完整人物档案、照片或附件内容。`application.log` 超过 2 MiB 时保留一个轮转副本；日志故障不得影响应用。

## Migration、备份与发布

- 正式结构变更必须经 EF Core Migration；不得以 `EnsureCreated` 升级正式库。
- 升级前先产生可验证备份；Migration 失败必须向用户报告，不能静默忽略。
- 启动时若已有数据库且存在待执行迁移，先在 `Backups` 下创建 SQLite 一致性快照并执行完整性检查；失败时不执行迁移。迁移失败时提示快照路径，快照保留供排查。新库和无需迁移的库不会生成此快照。
- 新版本谱匠进程启动前独占数据目录的 `.pujiang.lock`，退出时释放；同目录第二个新版本实例会提示并退出，不同目录可独立运行。不要手动删除正在使用的锁文件；锁文件保留不代表锁仍被占用。
- 迁移前快照仅包含数据库（包括库内图表配置），并非完整备份包；附件、数据库外配置及恢复流程仍待实现。文件锁只约束支持该协议的谱匠实例，使用旧版本或其他工具前应关闭谱匠，避免并发写入。
- 文件菜单“数据库备份与校验”提供版本 1 数据库专用 ZIP 创建及只读校验；创建不会覆盖已有文件。在线一致性快照生成时执行 SQLite 完整性检查，清单记录大小和 SHA-256；包校验检查结构、版本、大小和哈希，不验证来源真实性，也不在校验时再次执行 SQLite 完整性检查。数据库内图表配置随库包含，附件与数据库外配置不包含，数据库上限 2 GiB；恢复功能尚未实现，不可视为完整备份。界面人工验收状态见 [当前计划](CURRENT_PLAN.md)。
- 备份界面的“恢复前预检”在数据目录的 `Backups` 下创建临时副本，检查 SQLite 完整性、外键、迁移集合及与当前迁移结构的严格一致性。仅接受当前完整迁移集合及匹配结构，不支持旧版本迁移或自行扩展结构；临时文件清理失败会报告路径和错误。预检不替换当前库，不能替代恢复，也不证明数据内容或来源真实性。
- 开发版本在 `dev`，稳定版本在 `main`。正式发布目标为 Windows x64 self-contained；安装包、签名和自动更新尚未实施。

## 人工 UI 与性能验收

人工 UI 清单：启动、数据加载、人物新增/编辑/删除、树图、节点交互、打印预览、PDF/XPS 导出。当前目标模型 UI、备份恢复和新关系编辑为 Pending。

性能基线数据集包括基础三代、多房支、复杂关系契约、失考/非法关系，以及约 1,000 名虚构人物。日常测试只运行 Unit/Integration/Layout；Performance 单独触发，UI 为人工验收。
