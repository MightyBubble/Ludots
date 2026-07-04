using Arch.Core;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationCommandSourceSync
{
    public static bool SyncIfChanged(
        World world,
        System.Collections.Generic.Dictionary<string, object> globals,
        EntityCollectionStore collections,
        MassNavigationSimulationRuntime simulation)
    {
        if (!TryResolveOwner(world, globals, out Entity owner) ||
            !CommandSourceCollectionRuntime.TryGet(collections, owner, out EntityCollectionHandle handle, out EntityCollectionView view))
        {
            return ClearIfSelected(simulation);
        }

        if (handle.Revision == simulation.SelectionRevision &&
            view.Count == simulation.SelectedCount)
        {
            return false;
        }

        int count = view.Count;
        if (count <= 0)
        {
            simulation.SetSelection(System.ReadOnlySpan<Entity>.Empty, handle.Revision);
            return true;
        }

        Span<Entity> scratch = simulation.EnsureSelectionScratch(count);
        int written = collections.CopyEntities(handle, 0, scratch);
        simulation.SetSelection(scratch[..written], handle.Revision);
        return true;
    }

    private static bool ClearIfSelected(MassNavigationSimulationRuntime simulation)
    {
        if (simulation.SelectedCount <= 0)
        {
            return false;
        }

        simulation.ClearSelection();
        return true;
    }

    private static bool TryResolveOwner(
        World world,
        System.Collections.Generic.Dictionary<string, object> globals,
        out Entity owner)
    {
        owner = default;
        return globals.TryGetValue(Ludots.Core.Scripting.CoreServiceKeys.LocalPlayerEntity.Name, out object? ownerObj) &&
               ownerObj is Entity local &&
               world.IsAlive(local) &&
               (owner = local) != Entity.Null;
    }
}
