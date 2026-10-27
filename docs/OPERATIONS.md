# 开发、数据安全与发布规范

## 本地入口

在 Windows 与 .NET SDK 10.0.402 上执行：

```powershell
dotnet restore PuJiang.slnx
dotnet tool restore
dotnet build PuJiang.slnx --no-restore --configuration Release
dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --filter "Category!=Performance&Category!=UI"
dotnet publish ZupuPaiBan/ZupuPaiBan.csproj -c Release -r win-x64 --self-contained true
```

CI 使用相同 restore/build/test 入口。`Performance` 与显示窗口的 `UI` 测试须分别使用 `--filter "Category=Performance"` 和 `--filter "Category=UI"` 显式运行；人工 UI 验收另行记录。

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
6. 在仓库根目录执行 `dotnet restore PuJiang.slnx`、`dotnet build PuJiang.slnx --no-restore --configuration Release`、`dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --filter "Category!=Performance&Category!=UI"`。UI 和 Performance 测试须显式单独执行；不要把跳过或未运行的测试及人工 UI 验收报告为通过。迁移工具检查需要时再执行 `dotnet tool restore`；Windows 日常测试中的进程级用例需要 `dotnet` 和 Windows PowerShell 可执行文件。
7. Git 只同步已提交的文件；`.gitignore` 排除 `artifacts/`、常见数据库、日志和构建产物，但并不保证忽略所有附件或任意位置的测试数据，暂存前必须检查。正式用户数据位于仓库外，新电脑不会通过源码同步自动获得本机数据库、附件或配置。真实数据的迁移和恢复须单独安排；当前数据库专用备份包不含附件、配置，且正式恢复尚未实现。开发启动必须使用新的独立测试目录，详见下节。

每次检查点的提交、推送和实际远端结果必须分开核实；验收通过后的同步按 [AI 开发协议](../AGENTS.md#handoff-与-git-安全) 和用户的当前指令执行。

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
- 迁移前快照仅包含数据库，并非完整备份包；附件、配置及恢复流程仍待实现。文件锁只约束支持该协议的谱匠实例，使用旧版本或其他工具前应关闭谱匠，避免并发写入。
- 文件菜单“数据库备份与校验”提供版本 1 数据库专用 ZIP 创建及只读校验；创建不会覆盖已有文件。在线一致性快照生成时执行 SQLite 完整性检查，清单记录大小和 SHA-256；包校验检查结构、版本、大小和哈希，不验证来源真实性，也不在校验时再次执行 SQLite 完整性检查。附件与配置不包含，数据库上限 2 GiB；恢复功能尚未实现，不可视为完整备份。界面人工验收状态见 [当前计划](CURRENT_PLAN.md)。
- 备份界面的“恢复前预检”在数据目录的 `Backups` 下创建临时副本，检查 SQLite 完整性、外键、迁移集合及与当前迁移结构的严格一致性。仅接受当前完整迁移集合及匹配结构，不支持旧版本迁移或自行扩展结构；临时文件清理失败会报告路径和错误。预检不替换当前库，不能替代恢复，也不证明数据内容或来源真实性。
- 开发版本在 `dev`，稳定版本在 `main`。正式发布目标为 Windows x64 self-contained；安装包、签名和自动更新尚未实施。

## 人工 UI 与性能验收

人工 UI 清单：启动、数据加载、人物新增/编辑/删除、树图、节点交互、打印预览、PDF/XPS 导出。当前目标模型 UI、备份恢复和新关系编辑为 Pending。

性能基线数据集包括基础三代、多房支、复杂关系契约、失考/非法关系，以及约 1,000 名虚构人物。日常测试只运行 Unit/Integration/Layout；Performance 单独触发，UI 为人工验收。
