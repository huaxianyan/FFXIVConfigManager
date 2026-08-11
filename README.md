# FFXIVConfigManager

跨平台的 FINAL FANTASY XIV 角色配置管理工具。

## 项目目标

- 管理多个 FFXIV 配置目录和角色配置；
- 创建可校验、可回滚的版本化配置快照；
- 按配置类别在角色之间迁移设置；
- 首先支持 Windows，并为 Linux 和 macOS 保留跨平台能力。

## 技术栈

- C# / .NET 10 LTS
- Avalonia UI 12
- CommunityToolkit.Mvvm
- xUnit

项目采用模块化单体结构。Domain、Application 和通用 Infrastructure 不依赖具体桌面平台，Windows 专有能力位于独立平台项目中。

## 当前进度

已完成第一条可运行的垂直功能链路：

- 自动发现 Windows 国际服默认配置目录；
- 扫描并验证 `FFXIV_CHR` 角色目录；
- 识别 14 类已知角色配置文件；
- 默认忽略聊天日志、`.old` 文件和未知文件；
- 在 Avalonia 桌面界面展示角色、配置完整度和最后修改时间；
- 为 Domain、Application 和物理目录扫描提供自动化测试。

目前只提供只读扫描，尚未实现自定义配置源、快照、迁移和恢复。

## 开发

需要安装 .NET 10 SDK。

```powershell
dotnet restore
dotnet build FFXIVConfigManager.sln
dotnet test FFXIVConfigManager.sln
dotnet run --project src/FFXIVConfigManager.Desktop
```

## 旧版本

旧版项目 [`huaxianyan/ff14-ccmt`](https://github.com/huaxianyan/ff14-ccmt) 已停止维护并归档。新项目不会复制旧版代码、构建产物、日志或用户数据。
