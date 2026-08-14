# 本地化

桌面端用户文案集中存放在 `src/FFXIVConfigManager.Desktop/Localization/Strings.resx`，XAML 通过 `TrExtension` 读取静态文案，ViewModel 和桌面服务通过 `ITextLocalizer` 读取或格式化动态文案。新增语言时，应增加对应区域性的资源文件，例如 `Strings.en-US.resx`，不要在 XAML、ViewModel 或桌面服务中直接写入面向用户的文本。

当前版本只提供简体中文资源，资源选择遵循 .NET 的 `CurrentUICulture` 和标准回退规则。以后增加应用内语言切换时，应在切换 `CurrentUICulture` 后重建窗口或通知所有已绑定文案刷新，不能只替换部分控件文本。

## 文案和标点

中文文案遵循 [中文文案排版指北](https://github.com/sparanoid/chinese-copywriting-guidelines)，其中两项争议规则也作为项目规则执行：Markdown 超链接前后按语义留空格，简体中文使用直角引号「」和『』。

标点应以语义关系为准：关系紧密且共同描述同一主题的分句使用逗号连接，语义完整结束或主题明显转换时使用句号。分号只用于结构复杂的并列分项，尤其是并列项内部已经包含逗号的情况，不能把分号当作普通逗号或句号的替代品。

## 边界

资源文件保存产品界面文案，代码标识符、文件名、Manifest 字段和日志结构不参与翻译。Domain 和 Infrastructure 产生的异常消息属于诊断信息，未来支持其他语言时，Application 应将可预期故障转换为稳定的错误代码，再由桌面端映射为本地化文案，不能让 Domain 依赖桌面本地化服务。
