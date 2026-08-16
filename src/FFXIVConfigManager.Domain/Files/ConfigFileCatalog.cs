namespace FFXIVConfigManager.Domain.Files;

[Flags]
public enum ConfigScope
{
    None = 0,
    Hud = 1 << 0,
    Character = 1 << 1,
    Controls = 1 << 2,
    Hotbars = 1 << 3,
    Macros = 1 << 4,
    Gearsets = 1 << 5,
    InventoryOrder = 1 << 6,
    Miscellaneous = 1 << 7,
    PrivateData = 1 << 8,
    UiState = 1 << 9,
    AllKnownFiles = 1 << 10,
}

public enum FileSensitivity
{
    Normal,
    Cache,
    Private,
}

public sealed record ConfigFileDefinition(
    string FileName,
    string DisplayName,
    ConfigScope Scope,
    bool IncludedInSafeMigration,
    bool IncludedInDefaultBackup,
    FileSensitivity Sensitivity = FileSensitivity.Normal);

public static class ConfigFileCatalog
{
    private static readonly IReadOnlyDictionary<string, ConfigFileDefinition> DefinitionsByName =
        new Dictionary<string, ConfigFileDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACQ.DAT"] = new("ACQ.DAT", "近期悄悄话联系人", ConfigScope.PrivateData, false, false, FileSensitivity.Private),
            ["ADDON.DAT"] = new("ADDON.DAT", "HUD 与界面布局", ConfigScope.Hud, true, true),
            ["COMMON.DAT"] = new("COMMON.DAT", "角色通用设置", ConfigScope.Character, true, true),
            ["CONTROL0.DAT"] = new("CONTROL0.DAT", "键鼠模式设置", ConfigScope.Controls, true, true),
            ["CONTROL1.DAT"] = new("CONTROL1.DAT", "手柄模式设置", ConfigScope.Controls, true, true),
            ["GEARSET.DAT"] = new("GEARSET.DAT", "套装列表与即时肖像关联", ConfigScope.Gearsets, true, true),
            ["GS.DAT"] = new("GS.DAT", "九宫幻卡等数据", ConfigScope.Miscellaneous, false, true),
            ["HOTBAR.DAT"] = new("HOTBAR.DAT", "热键栏", ConfigScope.Hotbars, true, true),
            ["ITEMFDR.DAT"] = new("ITEMFDR.DAT", "物品搜索索引", ConfigScope.Miscellaneous, false, false, FileSensitivity.Cache),
            ["ITEMODR.DAT"] = new("ITEMODR.DAT", "物品排序", ConfigScope.InventoryOrder, false, true),
            ["KEYBIND.DAT"] = new("KEYBIND.DAT", "键位设置", ConfigScope.Controls, true, true),
            ["LOGFLTR.DAT"] = new("LOGFLTR.DAT", "消息窗口过滤器", ConfigScope.Character, true, true),
            ["MACRO.DAT"] = new("MACRO.DAT", "角色专用宏", ConfigScope.Macros, true, true),
            ["UISAVE.DAT"] = new("UISAVE.DAT", "界面状态、场地标点与肖像数据", ConfigScope.UiState, true, true),
        };

    public static IReadOnlyList<ConfigFileDefinition> All { get; } =
        DefinitionsByName.Values.OrderBy(definition => definition.FileName, StringComparer.Ordinal).ToArray();

    public static bool TryGet(string fileName, out ConfigFileDefinition definition) =>
        DefinitionsByName.TryGetValue(fileName, out definition!);
}
