using Ludots.Core.Engine;

namespace MassNavigationMod;

public static class MassNavigationIds
{
    public const string MapId = "mass_navigation";
    public const int RuntimeSpawnReceiptChannelId = 170_201;

    public static bool IsNavigationMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrentNavigationMap(GameEngine engine)
    {
        return engine.CurrentMapSession != null &&
               IsNavigationMap(engine.CurrentMapSession.MapId.Value);
    }
}

