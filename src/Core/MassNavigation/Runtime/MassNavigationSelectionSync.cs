using Arch.Core;
using Ludots.Core.Input.Selection;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationSelectionSync
{
    public static bool SyncIfChanged(
        World world,
        System.Collections.Generic.Dictionary<string, object> globals,
        SelectionRuntime selection,
        MassNavigationSimulationRuntime simulation)
    {
        if (!SelectionContextRuntime.TryDescribeCurrentView(world, globals, out SelectionViewDescriptor descriptor))
        {
            return false;
        }

        if (descriptor.Container.Revision == simulation.SelectionRevision &&
            descriptor.Container.MemberCount == simulation.SelectedCount)
        {
            return false;
        }

        int count = descriptor.Container.MemberCount;
        if (count <= 0)
        {
            simulation.SetSelection(System.ReadOnlySpan<Entity>.Empty, descriptor.Container.Revision);
            return true;
        }

        Span<Entity> scratch = simulation.EnsureSelectionScratch(count);
        int written = SelectionContextRuntime.CopyCurrentSelection(world, globals, scratch);
        simulation.SetSelection(scratch[..written], descriptor.Container.Revision);
        return true;
    }
}
