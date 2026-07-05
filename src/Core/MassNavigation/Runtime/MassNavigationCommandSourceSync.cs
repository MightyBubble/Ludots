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
            return ClearIfCommandSources(simulation);
        }

        if (handle.Revision == simulation.CommandSourceRevision &&
            view.Count == simulation.CommandSourceCount)
        {
            return false;
        }

        int count = view.Count;
        if (count <= 0)
        {
            simulation.SetCommandSources(System.ReadOnlySpan<Entity>.Empty, handle.Revision);
            return true;
        }

        Span<Entity> scratch = simulation.EnsureCommandSourceScratch(count);
        int written = collections.CopyEntities(handle, 0, scratch);
        simulation.SetCommandSources(scratch[..written], handle.Revision);
        return true;
    }

    private static bool ClearIfCommandSources(MassNavigationSimulationRuntime simulation)
    {
        if (simulation.CommandSourceCount <= 0)
        {
            return false;
        }

        simulation.ClearCommandSources();
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
