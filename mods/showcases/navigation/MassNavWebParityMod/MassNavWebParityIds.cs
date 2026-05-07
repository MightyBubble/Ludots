using Ludots.Core.Engine;

namespace MassNavWebParityMod;

public static class MassNavWebParityIds
{
    public const string LegacyMapId = "mass_nav_web_parity";

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
