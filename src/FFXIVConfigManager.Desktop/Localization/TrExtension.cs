using Avalonia.Markup.Xaml;

namespace FFXIVConfigManager.Desktop.Localization;

public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        ResourceTextLocalizer.Instance[key];
}
