# 角色肖像数据存储与处理边界

本文中的「角色肖像」指冒险者铭牌肖像和装备套装使用的即时肖像，不是角色创建界面的 `FFXIV_CHARA_01.dat`～`FFXIV_CHARA_40.dat` 形象预设。

## 已确认的存储位置

角色目录中不存在独立的 `BANNER.DAT`。肖像相关数据至少分布在以下两个磁盘文件中：

| 磁盘文件 | 已确认的数据 | 当前处理方式 |
| --- | --- | --- |
| `UISAVE.DAT` | 内部索引为 `0x17`（十进制 `23`）的 `BANNER` 分段，保存相机、姿势、表情、背景、边框、装饰和灯光等结构化肖像参数 | 完整角色备份按文件处理；肖像管理严格定位单条记录 |
| `GEARSET.DAT` | 装备套装条目中的 `BannerIndex`，用于关联即时肖像 | 完整角色备份按文件处理；肖像管理只读解析关联 |

`UISAVE.DAT` 是复合容器。`BANNER.DAT` 是客户端使用的虚拟分段名称，不是角色目录中的独立文件。`UISAVE.DAT` 还包含场地标点、历史记录和其他角色 UI 状态，因此不能把整个文件等同于肖像数据。

主要结构证据：

- [FFXIVClientStructs：UiSavePackModule](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Misc/UiSavePackModule.cs)
- [FFXIVClientStructs：BannerModule](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Misc/BannerModule.cs)
- [FFXIVClientStructs：RaptureGearsetModule](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Misc/RaptureGearsetModule.cs)
- [ff14_config_editor：ConfigUISave](https://github.com/MisakaCirno/ff14_config_editor/blob/master/FF14ConfigEditor/ConfigUISave.cs)

## 当前能力

默认角色备份和默认安全迁移均包含 `UISAVE.DAT` 与 `GEARSET.DAT`，因此会一起保存肖像主体和装备套装关联。现有事务流程仍按完整文件执行稳定性检查、暂存写入、SHA-256 校验、操作前恢复点和失败回滚。

完整角色备份恢复的影响范围不限于肖像：恢复 `UISAVE.DAT` 会同时恢复该容器中的其他 UI 和角色状态，恢复 `GEARSET.DAT` 会同时恢复套装列表及其关联。

独立的一级功能「肖像管理」提供更小粒度的操作。它只读解析 `GEARSET.DAT`，以游戏真实套装编号、职业图标和 UTF-8 原始套装名称识别已绑定肖像的套装；读取 BANNER 记录中的最后更新时间，但不解释或展示姿势、背景、边框等复杂字段。用户必须在来源栏选择一条具体肖像；恢复或迁移时还必须在目标角色选择一条具体套装肖像。

肖像管理支持：

- 角色 → 备份区：为选中的一条肖像创建独立方案，方案名称和备注均必填。
- 备份区 → 角色：把选中备份恢复到选中的目标套装肖像。
- 角色 → 角色：把选中的来源肖像迁移到选中的目标套装肖像。
- 相同数据源和备份区 → 备份区：禁止操作。

恢复和迁移不整体恢复 `UISAVE.DAT`，而是只替换目标 BANNER 记录中已验证的画面参数，同时保留目标记录身份、角色数据、装备校验和与关联状态。写入前会创建目标肖像恢复点；完整 `UISAVE.DAT` 仍通过暂存、事务日志、哈希校验、重新解析和失败回滚进行事务提交。`GEARSET.DAT` 不会被修改。

`ADDON.DAT` 继续用于 HUD 和界面布局。目前没有证据表明完整肖像参数保存在 `ADDON.DAT`。

## 尚未确认的范围

以下内容不能根据当前证据直接下结论：

- `BANNER` 分段在所有历史及未来游戏版本中的持久化记录布局。
- 冒险者铭牌除肖像外的全部字段是否都保存在本地。
- `ADVNOTE`、`DESCRI` 等分段与完整冒险者铭牌的具体关系。
- 新建、删除或重排 Banner 记录时的完整索引重映射规则。
- 装备校验和失配时，游戏接受或拒绝即时肖像的完整条件。

不得把基于客户端内存结构的推断直接当作稳定磁盘格式，也不得通过内存注入或操控游戏进程验证这些假设。

## 格式限制与写入约束

当前测试版只接受已验证的 `UISAVE.DAT` 容器、32 字节 BANNER 头部、118 字节固定记录以及预期字段标记顺序。`GEARSET.DAT` 只接受当前已验证的 100 个固定套装槽位布局。任何长度、版本、字段标记、UTF-8 名称或关联范围异常都会终止操作。

当前实现不新增、删除或重排肖像记录，只更新用户已选目标套装所关联的现有记录。该限制避免修改 `GEARSET.DAT` 或推测未验证的索引重映射规则。

分段级恢复必须持续满足：

- 保留 `UISAVE.DAT` 中所有无关及未知分段的原始字节。
- 不覆盖目标角色无关的 UI 状态和套装数据。
- 提供影响预览和明确的失败条件。
- 写入前创建目标肖像的独立恢复点，并在事务期间保留完整 `UISAVE.DAT` 回滚副本。
- 使用暂存文件、重新解析、哈希校验、事务日志和失败回滚。
- 游戏运行期间仍以文件稳定性检查处理并发修改，不禁止操作，也不操控游戏进程。
