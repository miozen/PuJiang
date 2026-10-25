# 开发、数据安全与发布规范

## 本地入口

在 Windows 与 .NET SDK 10.0.402 上执行：

```powershell
dotnet restore PuJiang.slnx
dotnet tool restore
dotnet build PuJiang.slnx --no-restore --configuration Release
dotnet test PuJiang.Tests/PuJiang.Tests.csproj --configuration Release --filter "Category!=Performance"
dotnet publish ZupuPaiBan/ZupuPaiBan.csproj -c Release -r win-x64 --self-contained true
```

CI 使用相同 restore/build/test 入口。`Performance` 须显式以 `--filter "Category=Performance"` 运行；人工 UI 验收不属于日常 test 命令。

迁移工具固定在仓库的 `dotnet-tools.json`。在 Release 构建后，可用下列无连接检查验证当前模型与快照一致，且不会创建或访问用户数据库：

```powershell
dotnet tool run dotnet-ef migrations has-pending-model-changes --project ZupuPaiBan/ZupuPaiBan.csproj --startup-project ZupuPaiBan/ZupuPaiBan.csproj --context ZupuDbContext --configuration Release --no-build
```

## 数据与日志目录

正式用户数据根目录为 `%LOCALAPPDATA%\PuJiang`，程序安装目录不得保存可变用户数据。约定子目录为：`pujiang.db`、`Attachments`、`Backups`、`Exports`、`Logs`。照片和文献附件应放入 `Attachments`；数据库、附件、配置和格式版本是未来备份恢复范围。

日志只记录启动、未处理异常和必要技术上下文，不记录完整人物档案、照片或附件内容。`application.log` 超过 2 MiB 时保留一个轮转副本；日志故障不得影响应用。

## Migration、备份与发布

- 正式结构变更必须经 EF Core Migration；不得以 `EnsureCreated` 升级正式库。
- 升级前先产生可验证备份；Migration 失败必须向用户报告，不能静默忽略。
- 备份恢复验收应覆盖数据库、附件、必要配置和格式版本；P0-0 只定义规范，不提供备份实现。
- 开发版本在 `dev`，稳定版本在 `main`。正式发布目标为 Windows x64 self-contained；安装包、签名和自动更新尚未实施。

## 人工 UI 与性能验收

人工 UI 清单：启动、数据加载、人物新增/编辑/删除、树图、节点交互、打印预览、PDF/XPS 导出。当前目标模型 UI、备份恢复和新关系编辑为 Pending。

性能基线数据集包括基础三代、多房支、复杂关系契约、失考/非法关系，以及约 1,000 名虚构人物。日常测试只运行 Unit/Integration/Layout；Performance 单独触发，UI 为人工验收。
