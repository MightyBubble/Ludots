using Ludots.Core.Engine;

namespace Ludots.Core.MassNavigation;

public static class MassNavigationIds
{
    public static bool IsNavigationMap(GameEngine engine, string? mapId)
    {
        if (engine == null)
        {
            throw new System.ArgumentNullException(nameof(engine));
        }

        if (engine.GetService(MassNavigationKeys.RuntimeBinding) is not Runtime.MassNavigationRuntimeBinding binding ||
            binding.Current is not Runtime.MassNavigationSimulationRuntime simulation)
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

    public static bool IsNavigationRuntimeReady(GameEngine engine, string? mapId)
    {
        if (!IsNavigationMap(engine, mapId))
        {
            return false;
        }

        return engine.GetService(MassNavigationKeys.RuntimeBinding) is Runtime.MassNavigationRuntimeBinding binding &&
               binding.IsReady &&
               string.Equals(binding.CurrentMapId.Value, mapId, System.StringComparison.Ordinal);
    }

    public static bool IsCurrentNavigationRuntimeReady(GameEngine engine)
    {
        return engine.CurrentMapSession != null &&
               IsNavigationRuntimeReady(engine, engine.CurrentMapSession.MapId.Value);
    }
}
