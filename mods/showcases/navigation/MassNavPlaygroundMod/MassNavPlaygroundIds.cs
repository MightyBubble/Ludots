namespace MassNavPlaygroundMod;

internal static class MassNavPlaygroundIds
{
    public const string LegacyMapId = "mass_nav_playground";

    public static bool IsPlaygroundMap(string? mapId)
    {
        return string.Equals(mapId, LegacyMapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
