using Ludots.Core.Engine;

namespace MassNavPlaygroundMod;

internal static class MassNavPlaygroundIds
{
    public const string LegacyMapId = "mass_nav_playground";

    public static bool IsPlaygroundMap(string? mapId)
    {
        return string.Equals(mapId, LegacyMapId, System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrentPlaygroundMap(GameEngine engine)
    {
        return engine.CurrentMapSession != null &&
               IsPlaygroundMap(engine.CurrentMapSession.MapId.Value);
    }
}
