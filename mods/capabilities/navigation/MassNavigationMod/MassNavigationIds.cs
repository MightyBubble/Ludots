using Ludots.Core.Engine;

namespace MassNavigationMod;

public static class MassNavigationIds
{
    public const string RuntimeSpawnReceiptChannelKey = "massNavigation.runtimeSpawnReceipts";

    public static bool IsNavigationMap(GameEngine engine, string? mapId)
    {
        if (engine == null)
        {
            throw new System.ArgumentNullException(nameof(engine));
        }

        if (engine.GetService(MassNavigationKeys.SimulationRuntime) is not Runtime.MassNavigationSimulationRuntime simulation)
        {
            return false;
        }

        return string.Equals(mapId, simulation.Config.MapId, System.StringComparison.Ordinal);
    }

    public static bool IsCurrentNavigationMap(GameEngine engine)
    {
        return engine.CurrentMapSession != null &&
               IsNavigationMap(engine, engine.CurrentMapSession.MapId.Value);
    }
}

