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
- 添加和移除国服、国际服或其他自定义配置源；
- 使用 `%LOCALAPPDATA%/FFXIVConfigManager/settings.json` 持久化用户设置；
- 原子写入设置文件，并拒绝读取高于当前程序支持版本的数据；
- 扫描并验证 `FFXIV_CHR` 角色目录；
- 为不同配置源下的角色保存独立别名；
- 识别 14 类已知角色配置文件；
- 默认忽略聊天日志、`.old` 文件、隐私数据、缓存和未知文件；
- 创建版本化 `.ffxivconfig.zip` 角色快照；
- 在读取前后检查文件大小和修改时间，变化时自动重试；
- 使用 Manifest 记录来源、时间、文件大小、时间戳和 SHA-256；
- 创建完成后立即重新读取归档并进行完整性校验；
- 拒绝路径穿越、重复条目、未声明条目和超限归档；
- 从快照及其 Manifest 重建历史索引，不依赖不可恢复的数据库；
- 展示有效和损坏的快照，并支持按角色、配置源和状态筛选；
- 在恢复前重新校验快照，并逐文件比较当前角色的稳定 SHA-256；
- 恢复前自动创建目标恢复点，使用暂存、事务日志、逐文件替换和写后校验；
- 写入失败或取消时自动回滚，启动时继续处理意外中断的恢复事务；
- 支持在任意两个本地角色之间按安全配置范围预览和迁移；
- 迁移时同时保存迁移源快照与目标操作前恢复点；
- 不因检测到 FFXIV 或 XIVLauncher 正在运行而限制操作；
- 单实例运行并保存本地诊断日志；
- 在 Avalonia 桌面界面展示角色、配置完整度和最后修改时间；
- 为 Domain、Application、设置存储、快照、恢复事务和迁移提供自动化测试。

项目已进入 Windows 手动验收阶段。Linux 和 macOS 尚未进行兼容性验证。

## 开发

需要安装 .NET 10 SDK。

```powershell
dotnet restore
dotnet build FFXIVConfigManager.sln
dotnet test FFXIVConfigManager.sln
dotnet run --project src/FFXIVConfigManager.Desktop
```

创建 Windows 便携包：

```powershell
./scripts/publish-windows.ps1
```

输出位于：

```text
artifacts/FFXIVConfigManager-win-x64.zip
```

## 旧版本

旧版项目 [`huaxianyan/ff14-ccmt`](https://github.com/huaxianyan/ff14-ccmt) 已停止维护并归档。新项目不会复制旧版代码、构建产物、日志或用户数据。
