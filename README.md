# 谱匠（PuJiang）

一款离线优先的 Windows 桌面族谱管理与排版工具：录入家族成员信息，自动生成可打印的世系树图，支持 A2/A3/A4 纸张打印与导出。

## 功能特性

- **成员管理**：录入姓名、性别、生卒日期、配偶、生平简介、照片，通过父子关系自动构建家族树
- **自动排版**：经典族谱树形布局算法，自动计算各代位置、配偶并排、连线生成
- **可调版式**：节点尺寸、间距、字号、标题、纸张（A2/A3/A4）、横竖版均可调节，改动实时预览
- **打印预览**：所见即所得预览窗口，缩放浏览，直接打印或导出 XPS
- **本地存储**：SQLite 单文件数据库（`zupu.db`），数据完全留在本机，无任何网络上传

## 技术栈

| 组件 | 说明 |
|---|---|
| .NET 10 (WPF) | Windows 桌面框架 |
| CommunityToolkit.Mvvm 8.4 | MVVM 架构与源代码生成器 |
| EF Core + SQLite | 本地数据持久化 |

## 项目结构

```
ZupuPaiBan/
├── Models/            # 数据模型（FamilyMember、LayoutSettings）
├── ViewModels/        # MVVM ViewModel（主界面/成员编辑/打印预览）
├── Views/             # 对话框与窗口（成员编辑、打印预览、关于）
├── Services/          # 树形布局引擎、打印/导出服务
├── Data/              # EF Core DbContext 与数据访问层
├── Converters/        # XAML 值转换器
└── Resources/         # 全局样式
```

## 环境要求

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 打印功能依赖系统打印机（含 Microsoft XPS Document Writer）

## 构建与运行

```bash
cd ZupuPaiBan
dotnet run
```

首次运行会在应用程序目录创建 `zupu.db` 数据库文件。

## 使用简介

1. 启动后进入主界面，左侧为成员树，右侧为成员信息
2. 通过「添加成员」录入始祖，再逐代添加子女（自动计算世代与排行）
3. 点击「排版预览」进入预览窗口，调整纸张、字号、间距等参数
4. 满意后直接打印，或通过 XPS Document Writer 导出文件

## Roadmap

- [ ] PDF 直接导出
- [ ] 苏式/欧式等传统族谱版式
- [ ] 吊线图（竖版世系图）
- [ ] 数据导入导出（GEDCOM / Excel）
- [ ] 照片自动裁剪与圆形头像

## License

MIT License. 详见 [LICENSE](LICENSE)。
