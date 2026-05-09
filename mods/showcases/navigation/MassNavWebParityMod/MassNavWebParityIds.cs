using Ludots.Core.Engine;

namespace MassNavWebParityMod;

public static class MassNavWebParityIds
{
    public const string MapId = "mass_nav_web_parity";
    public const int RuntimeSpawnReceiptChannelId = 170_201;

    public static bool IsPlaygroundMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrentPlaygroundMap(GameEngine engine)
    {
        return engine.CurrentMapSession != null &&
               IsPlaygroundMap(engine.CurrentMapSession.MapId.Value);
    }
}
