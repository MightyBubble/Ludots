using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Input.Selection;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationSelectionAccess
{
    public static int GetCurrentCount(World world, Dictionary<string, object> globals)
    {
        return SelectionContextRuntime.GetCurrentCount(world, globals);
    }

    public static int CopyCurrentSelection(
        World world,
        Dictionary<string, object> globals,
        MassNavigationSimulationRuntime simulation,
        Span<Entity> destination)
    {
        int required = SelectionContextRuntime.GetCurrentCount(world, globals);
        if (required <= 0)
        {
            return 0;
        }

        if (required > destination.Length)
        {
            destination = simulation.EnsureSelectionScratch(required);
        }

        return SelectionContextRuntime.CopyCurrentSelection(world, globals, destination);
    }

    public static void RefreshFlowSelectedFlags(
        World world,
        Dictionary<string, object> globals,
        MassNavigationSimulationRuntime simulation)
    {
        int count = SelectionContextRuntime.GetCurrentCount(world, globals);
        if (count <= 0)
        {
            simulation.MassNavigationFlow.SetSelectedFlags(simulation.AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        Span<Entity> scratch = simulation.EnsureSelectionScratch(count);
        int written = SelectionContextRuntime.CopyCurrentSelection(world, globals, scratch);
        simulation.MassNavigationFlow.SetSelectedFlags(simulation.AgentState, scratch[..written]);
    }
}
