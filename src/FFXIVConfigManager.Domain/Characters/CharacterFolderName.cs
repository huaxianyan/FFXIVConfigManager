namespace FFXIVConfigManager.Domain.Characters;

public readonly record struct CharacterFolderName
{
    public const string Prefix = "FFXIV_CHR";
    public const int IdentifierLength = 16;

    private CharacterFolderName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out CharacterFolderName folderName)
    {
        folderName = default;

        if (value is null ||
            value.Length != Prefix.Length + IdentifierLength ||
            !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var identifier = value.AsSpan(Prefix.Length);
        foreach (var character in identifier)
        {
            if (!IsHexadecimal(character))
            {
                return false;
            }
        }

        folderName = new CharacterFolderName($"{Prefix}{identifier.ToString().ToUpperInvariant()}");
        return true;
    }

    public static CharacterFolderName Create(string value) =>
        TryCreate(value, out var folderName)
            ? folderName
            : throw new ArgumentException("角色配置目录名称格式无效。", nameof(value));

    public override string ToString() => Value ?? string.Empty;

    private static bool IsHexadecimal(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
