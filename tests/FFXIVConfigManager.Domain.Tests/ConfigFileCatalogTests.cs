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
    public void Catalog_IncludesUiSaveInSafeMigration()
    {
        Assert.True(ConfigFileCatalog.TryGet("UISAVE.DAT", out var uiSave));

        Assert.Equal(ConfigScope.UiState, uiSave.Scope);
        Assert.True(uiSave.IncludedInSafeMigration);
        Assert.True(uiSave.IncludedInDefaultBackup);
    }

    [Fact]
    public void Catalog_LookupIsCaseInsensitive()
    {
        Assert.True(ConfigFileCatalog.TryGet("hotbar.dat", out var definition));
        Assert.Equal(ConfigScope.Hotbars, definition.Scope);
    }
}
