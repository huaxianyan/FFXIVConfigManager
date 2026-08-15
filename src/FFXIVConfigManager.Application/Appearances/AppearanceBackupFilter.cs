using FFXIVConfigManager.Domain.Appearances;

namespace FFXIVConfigManager.Application.Appearances;

public static class AppearanceBackupFilter
{
    public static bool Matches(
        AppearanceMetadata? appearance,
        AppearanceRace? race,
        AppearanceGender? gender,
        string? commentSearch)
    {
        var search = commentSearch?.Trim() ?? string.Empty;
        if (appearance is null)
        {
            return race is null && gender is null && search.Length == 0;
        }

        return (race is null || appearance.Race == race) &&
               (gender is null || appearance.Gender == gender) &&
               (search.Length == 0 ||
                appearance.Comment.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}
