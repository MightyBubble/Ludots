using System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Runtime;

internal static class MassNavigationRouteExecutionBootstrap
{
    public static MassNavigationRouteExecutionSink? Ensure(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);

        IPathService? pathService = engine.GetService(CoreServiceKeys.PathService);
        PathStore? pathStore = engine.GetService(CoreServiceKeys.PathStore);
        PathingConfig? pathingConfig = engine.GetService(CoreServiceKeys.PathingConfig);
        if (pathService == null && pathStore == null && pathingConfig == null)
        {
            engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
            return null;
        }

        if (pathService == null || pathStore == null || pathingConfig == null)
        {
            throw new InvalidOperationException(
                "MassNavigation route execution requires PathService, PathStore, and PathingConfig to be registered together.");
        }

        MassNavigationRouteExecutionSink? existing = engine.GetService(MassNavigationKeys.RouteExecutionSink);
        if (existing != null && existing.IsBoundTo(pathService, pathStore, pathingConfig))
        {
            return existing;
        }

        MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
        var sink = new MassNavigationRouteExecutionSink(
            pathService,
            pathStore,
            pathingConfig,
            capacity.RouteStateCapacity,
            capacity.RouteWaypointCapacityPerAgent);
        engine.SetService(MassNavigationKeys.RouteExecutionSink, sink);
        return sink;
    }
}
