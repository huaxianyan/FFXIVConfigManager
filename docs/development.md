# 开发与发布

本文面向希望构建、测试、修改或发布 FFXIVConfigManager 的开发者。用户功能介绍请阅读项目根目录的 [`README.md`](../README.md)。

## 技术栈

- C# / .NET 10 LTS
- Avalonia UI 12
- CommunityToolkit.Mvvm
- xUnit

## 代码结构

项目采用模块化单体和 MVVM：

- `FFXIVConfigManager.Domain`：角色、文件目录、备份 Manifest、角色形象和肖像等领域模型。
- `FFXIVConfigManager.Application`：扫描、备份、恢复、迁移和更新等用例及接口。
- `FFXIVConfigManager.Infrastructure`：物理文件系统、ZIP、JSON、GitHub Release 和事务实现。
- `FFXIVConfigManager.Platform.Windows`：Windows 默认路径发现和单文件自更新。
- `FFXIVConfigManager.Desktop`：Avalonia UI、MVVM、本地化和桌面交互。
- `tests/`：Domain、Application 和 Integration 自动化测试。

Domain、Application 和通用 Infrastructure 不依赖 Windows 专有 API。当前正式支持 Windows；Linux 和 macOS 仅保留平台边界，尚未完成兼容性验证。

## 本地构建

需要安装仓库 `global.json` 指定的 .NET 10 SDK。

```powershell
dotnet restore
dotnet build FFXIVConfigManager.sln
dotnet test FFXIVConfigManager.sln
dotnet run --project src/FFXIVConfigManager.Desktop
```

提交前应执行：

```powershell
dotnet format FFXIVConfigManager.sln --verify-no-changes
dotnet test FFXIVConfigManager.sln
dotnet build FFXIVConfigManager.sln -c Release
```

## Windows 便携包

```powershell
./scripts/publish-windows.ps1
```

输出：

```text
artifacts/FFXIVConfigManager-win-x64.zip
artifacts/FFXIVConfigManager-win-x64.zip.sha256
```

## 自动发布

推送符合 `v<major>.<minor>.<patch>` 格式的标签后，GitHub Actions 会验证标签版本，执行格式检查、Release 构建和全部测试，然后创建 GitHub Release 并上传 Windows 便携包及 SHA-256。

发布前必须创建 `docs/release-notes/<标签>.md`，例如 `docs/release-notes/v0.4.0.md`。发布说明应使用简体中文，并遵循项目的中文文案排版规则。工作流找不到对应文件时会拒绝发布。

```powershell
git tag -a v0.4.0 -m "FFXIVConfigManager 0.4.0"
git push origin v0.4.0
```

不要改写已经发布的标签；修复应使用新的递增版本。

## 实现约束

- UI 不直接操作文件系统，由 Application 用例或服务编排。
- Domain 不依赖 UI、操作系统、数据库或具体文件系统。
- 备份及其 Manifest 是事实来源，索引和缓存必须可重建。
- 不解析或修改未经充分验证的 FFXIV 二进制结构。
- 文件写入必须考虑稳定读取、暂存、校验、事务日志和失败回滚。
- 不因检测到 FFXIV 或 XIVLauncher 进程而禁止操作，也不注入、结束或操控游戏进程。
- 默认不收集聊天日志等隐私数据。
- 覆盖操作必须有明确目标、影响提示和恢复路径。

## 相关技术文档

| 文档 | 内容 |
| --- | --- |
| [`file-transactions.md`](file-transactions.md) | 文件事务、恢复点和失败回滚 |
| [`snapshot-format.md`](snapshot-format.md) | 角色配置备份格式 |
| [`settings-backup-format.md`](settings-backup-format.md) | 软件设置备份格式 |
| [`appearance-backup-format.md`](appearance-backup-format.md) | 角色形象备份格式 |
| [`portrait-storage.md`](portrait-storage.md) | 肖像存储证据和处理边界 |
| [`portrait-backup-format.md`](portrait-backup-format.md) | 独立肖像备份格式 |
| [`automatic-update.md`](automatic-update.md) | 自动更新、替换和回滚 |
| [`localization.md`](localization.md) | 本地化资源和文案规则 |
| [`manual-test-plan.md`](manual-test-plan.md) | Windows 手动验收计划 |

## 贡献与许可证

项目代码采用 [`GPL-3.0-only`](../LICENSE)。可以收费分发和提供商业服务，但分发本项目或其衍生版本时，必须遵守 GPL v3，包括提供对应源代码并以 GPL v3 授权衍生作品。

第三方依赖、FINAL FANTASY XIV 名称、商标和游戏资源不因本项目许可证而改变其权利归属，详见 [`legal.md`](legal.md)。
