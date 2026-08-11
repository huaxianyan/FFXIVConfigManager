namespace FFXIVConfigManager.Domain.Profiles;

public enum GameRegion
{
    International,
    China,
    Custom,
}

public sealed record GameProfile
{
    public GameProfile(Guid id, string name, GameRegion region, string configRoot)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("配置源 ID 不能为空。", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("配置源名称不能为空。", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(configRoot))
        {
            throw new ArgumentException("配置根目录不能为空。", nameof(configRoot));
        }

        Id = id;
        Name = name.Trim();
        Region = region;
        ConfigRoot = configRoot;
    }

    public Guid Id { get; }

    public string Name { get; }

    public GameRegion Region { get; }

    public string ConfigRoot { get; }
}
