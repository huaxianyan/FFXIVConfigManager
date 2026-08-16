using FFXIVConfigManager.Domain.Files;

namespace FFXIVConfigManager.Domain.Tests;

public sealed class ConfigFileCatalogTests
{
    [Fact]
    public void Catalog_ExcludesPrivateAndCacheFilesFromDefaultBackup()
    {
        Assert.True(ConfigFileCatalog.TryGet("ACQ.DAT", out var contacts));
        Assert.True(ConfigFileCatalog.TryGet("ITEMFDR.DAT", out var itemSearchIndex));

        Assert.Equal(FileSensitivity.Private, contacts.Sensitivity);
        Assert.False(contacts.IncludedInDefaultBackup);
        Assert.Equal(FileSensitivity.Cache, itemSearchIndex.Sensitivity);
        Assert.False(itemSearchIndex.IncludedInDefaultBackup);
    }

    [Fact]
    public void Catalog_IncludesPortraitDataAndGearsetLinksInSafeMigration()
    {
        Assert.True(ConfigFileCatalog.TryGet("UISAVE.DAT", out var uiSave));
        Assert.True(ConfigFileCatalog.TryGet("GEARSET.DAT", out var gearsets));

        Assert.Equal(ConfigScope.UiState, uiSave.Scope);
        Assert.Contains("肖像数据", uiSave.DisplayName, StringComparison.Ordinal);
        Assert.True(uiSave.IncludedInSafeMigration);
        Assert.True(uiSave.IncludedInDefaultBackup);

        Assert.Equal(ConfigScope.Gearsets, gearsets.Scope);
        Assert.Contains("即时肖像关联", gearsets.DisplayName, StringComparison.Ordinal);
        Assert.True(gearsets.IncludedInSafeMigration);
        Assert.True(gearsets.IncludedInDefaultBackup);
    }

    [Fact]
    public void Catalog_LookupIsCaseInsensitive()
    {
        Assert.True(ConfigFileCatalog.TryGet("hotbar.dat", out var definition));
        Assert.Equal(ConfigScope.Hotbars, definition.Scope);
    }
}
