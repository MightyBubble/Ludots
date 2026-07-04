using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.EntityView;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationSelectionAccess
{
    public static EntityViewRuntimeConfig RequireEntityViewConfig(Dictionary<string, object> globals)
    {
        if (globals.TryGetValue(CoreServiceKeys.EntityViewConfig.Name, out object? configObj) &&
            configObj is EntityViewRuntimeConfig config)
        {
            return config;
        }

        throw new InvalidOperationException("MassNavigation command source access requires EntityViewConfig.");
    }

    public static int GetCurrentCount(World world, Dictionary<string, object> globals)
    {
        EntityViewRuntimeConfig config = RequireEntityViewConfig(globals);
        return EntityViewRuntime.GetCommandSourceCount(world, globals, config);
    }

    public static int CopyCurrentSelection(
        World world,
        Dictionary<string, object> globals,
        MassNavigationSimulationRuntime simulation,
        Span<Entity> destination)
    {
        EntityViewRuntimeConfig config = RequireEntityViewConfig(globals);
        int required = EntityViewRuntime.GetCommandSourceCount(world, globals, config);
        if (required <= 0)
        {
            return 0;
        }

        if (required > destination.Length)
        {
            destination = simulation.EnsureSelectionScratch(required);
        }

        return EntityViewRuntime.CopyCommandSourceEntities(world, globals, config, destination);
    }

    public static void RefreshFlowSelectedFlags(
        World world,
        Dictionary<string, object> globals,
        MassNavigationSimulationRuntime simulation)
    {
        EntityViewRuntimeConfig config = RequireEntityViewConfig(globals);
        int count = EntityViewRuntime.GetCommandSourceCount(world, globals, config);
        if (count <= 0)
        {
            simulation.MassNavigationFlow.SetSelectedFlags(simulation.AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        Span<Entity> scratch = simulation.EnsureSelectionScratch(count);
        int written = EntityViewRuntime.CopyCommandSourceEntities(world, globals, config, scratch);
        simulation.MassNavigationFlow.SetSelectedFlags(simulation.AgentState, scratch[..written]);
    }

    public static bool TryGetCurrentCommandSourceHandle(
        World world,
        Dictionary<string, object> globals,
        out Entity owner,
        out EntityCollectionHandle handle)
    {
        EntityViewRuntimeConfig config = RequireEntityViewConfig(globals);
        return EntityViewRuntime.TryGetCommandSourceHandle(world, globals, config, out owner, out handle);
    }
}
