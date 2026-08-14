using System.Globalization;
using System.Resources;

namespace FFXIVConfigManager.Desktop.Localization;

public sealed class ResourceTextLocalizer : ITextLocalizer
{
    private static readonly ResourceManager Resources = new(
        "FFXIVConfigManager.Desktop.Localization.Strings",
        typeof(ResourceTextLocalizer).Assembly);

    public static ResourceTextLocalizer Instance { get; } = new();

    private ResourceTextLocalizer()
    {
    }

    public string this[string key] =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing localization resource: {key}");

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, this[key], arguments);
}
