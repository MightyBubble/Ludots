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

    public static bool TryGetCurrentNavigationRuntime(
        GameEngine engine,
        out Runtime.MassNavigationSimulationRuntime simulation)
    {
        simulation = null!;
        if (engine.CurrentMapSession == null ||
            engine.GetService(MassNavigationKeys.RuntimeBinding) is not Runtime.MassNavigationRuntimeBinding binding ||
            !binding.IsReady ||
            binding.Current is not Runtime.MassNavigationSimulationRuntime current ||
            !string.Equals(binding.CurrentMapId.Value, engine.CurrentMapSession.MapId.Value, System.StringComparison.Ordinal))
        {
            return false;
        }

        simulation = current;
        return true;
    }

    internal static bool TryGetActiveNavigationRuntime(
        GameEngine engine,
        out Runtime.MassNavigationSimulationRuntime simulation)
    {
        simulation = null!;
        if (engine.CurrentMapSession == null ||
            engine.GetService(MassNavigationKeys.RuntimeBinding) is not Runtime.MassNavigationRuntimeBinding binding ||
            binding.Current is not Runtime.MassNavigationSimulationRuntime current ||
            !string.Equals(binding.CurrentMapId.Value, engine.CurrentMapSession.MapId.Value, System.StringComparison.Ordinal))
        {
            return false;
        }

        simulation = current;
        return true;
    }

    /// <summary>
    /// 已 Activate（map 聚焦）但绑定 pass 可能尚未完成的运行时——供 MapLoaded 时刻的
    /// 域级写（SetDomainMarchTarget 挂起语义）解析；逐单位写仍要求 IsReady。
    /// </summary>
    internal static Runtime.MassNavigationSimulationRuntime? TryGetActivatingNavigationRuntime(GameEngine engine)
    {
        if (engine.CurrentMapSession == null ||
            engine.GetService(MassNavigationKeys.RuntimeBinding) is not Runtime.MassNavigationRuntimeBinding binding ||
            binding.Current is not Runtime.MassNavigationSimulationRuntime current ||
            !string.Equals(binding.CurrentMapId.Value, engine.CurrentMapSession.MapId.Value, System.StringComparison.Ordinal))
        {
            return null;
        }

        return current;
    }

    internal static void PublishPreparedWhenBindingComplete(
        GameEngine engine,
        Runtime.MassNavigationSimulationRuntime simulation)
    {
        if (!simulation.RuntimeBindingPreparationComplete ||
            engine.CurrentMapSession == null ||
            engine.GetService(MassNavigationKeys.RuntimeBinding) is not Runtime.MassNavigationRuntimeBinding binding ||
            binding.IsReady ||
            !ReferenceEquals(binding.Current, simulation) ||
            !string.Equals(binding.CurrentMapId.Value, engine.CurrentMapSession.MapId.Value, System.StringComparison.Ordinal))
        {
            return;
        }

        binding.MarkPrepared(engine.CurrentMapSession.MapId, simulation);
    }
}
