# FFXIVConfigManager

跨平台的 FINAL FANTASY XIV 角色配置管理工具。

## 项目目标

- 管理多个 FFXIV 配置目录和角色配置。
- 创建可校验、可回滚的版本化配置备份。
- 按配置类别在角色之间迁移设置。
- 首先支持 Windows，并为 Linux 和 macOS 保留跨平台能力。

## 技术栈

- C# / .NET 10 LTS
- Avalonia UI 12
- CommunityToolkit.Mvvm
- xUnit

项目采用模块化单体结构，Domain、Application 和通用 Infrastructure 不依赖具体桌面平台，Windows 专有能力位于独立平台项目中。

## 当前进度

已完成第一条可运行的垂直功能链路：

- 自动发现 Windows 国际服默认配置目录。
- 添加和移除国服、国际服或其他自定义配置源。
- 使用 `%LOCALAPPDATA%/FFXIVConfigManager/settings.json` 持久化用户设置。
- 原子写入设置文件，并拒绝读取高于当前程序支持版本的数据。
- 扫描并验证 `FFXIV_CHR` 角色目录。
- 为不同配置源下的角色保存独立标记。
- 识别 14 类已知角色配置文件。
- 默认忽略聊天日志、`.old` 文件、隐私数据、缓存和未知文件。
- 创建版本化 `.ffxivconfig.zip` 角色备份。
- 在读取前后检查文件大小和修改时间，变化时自动重试。
- 使用 Manifest 记录来源、时间、文件大小、时间戳和 SHA-256。
- 创建完成后立即重新读取归档并进行完整性校验。
- 拒绝路径穿越、重复条目、未声明条目和超限归档。
- 从备份及其 Manifest 重建历史索引，不依赖不可恢复的数据库。
- 以角色为单位汇总备份数量、完整性和最近备份时间，并支持按角色、配置源和状态筛选。
- 角色管理和备份页面通过独立次级窗口选择具体备份，预览后可执行恢复或删除。
- 在恢复前重新校验备份，并逐文件比较当前角色的稳定 SHA-256。
- 恢复前自动创建目标恢复点，使用暂存、事务日志、逐文件替换和写后校验。
- 写入失败或取消时自动回滚，启动时继续处理意外中断的恢复事务。
- 支持在任意两个本地角色之间按安全配置范围预览和迁移。
- 默认迁移 `UISAVE.DAT` 中的界面状态与场地标点，并提供全部 14 个已知文件高级模式。
- 迁移时同时保存迁移源备份与目标操作前恢复点。
- 在备份目录中维护单份软件设置备份，可按角色标记或自定义配置源选择备份和恢复范围。
- 支持查看和删除任意历史备份，本机缺少对应角色时仍可查看备份内容。
- 不因检测到 FFXIV 或 XIVLauncher 正在运行而限制操作。
- 单实例运行并保存本地诊断日志。
- 在 Avalonia 桌面界面展示角色、已知文件数量和最后修改时间。
- 将桌面端用户文案集中到标准 `.resx` 资源，为后续 i18n 扩展保留清晰边界。
- 为 Domain、Application、设置存储、备份、恢复事务和迁移提供自动化测试。

项目首个版本正式支持 Windows，Linux 和 macOS 尚未进行兼容性验证。

## 开发

需要安装 .NET 10 SDK，本地化资源结构和文案规则见 [`docs/localization.md`](docs/localization.md)。

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

## 发布

推送符合 `v<major>.<minor>.<patch>` 格式的标签后，GitHub Actions 会自动执行格式检查、Release 构建和全部测试。验证通过后，工作流创建对应的 GitHub Release，并上传以下文件：

- `FFXIVConfigManager-win-x64.zip`
- `FFXIVConfigManager-win-x64.zip.sha256`

例如发布 `0.1.0`：

```powershell
git tag -a v0.1.0 -m "FFXIVConfigManager 0.1.0"
git push origin v0.1.0
```

## 旧版本

旧版项目 [`huaxianyan/ff14-ccmt`](https://github.com/huaxianyan/ff14-ccmt) 已停止维护并归档，新项目不会复制旧版代码、构建产物、日志或用户数据。
