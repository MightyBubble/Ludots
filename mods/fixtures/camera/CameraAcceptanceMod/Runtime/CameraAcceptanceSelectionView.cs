using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;

namespace CameraAcceptanceMod.Runtime
{
    internal static class CameraAcceptanceSelectionView
    {
        public static int CopySelectedEntities(World world, Dictionary<string, object> globals, Span<Entity> destination)
        {
            return TryResolveLocalCommandSourceOwner(world, globals, out Entity owner)
                ? EntityCollectionContextRuntime.Copy(globals, owner, EntityCollectionKeys.CommandSource, destination)
                : 0;
        }

        public static Entity[] SnapshotSelectedEntities(World world, Dictionary<string, object> globals)
        {
            return TryResolveLocalCommandSourceOwner(world, globals, out Entity owner)
                ? EntityCollectionContextRuntime.Snapshot(globals, owner, EntityCollectionKeys.CommandSource)
                : Array.Empty<Entity>();
        }

        private static bool TryResolveLocalCommandSourceOwner(World world, Dictionary<string, object> globals, out Entity owner)
        {
            owner = Entity.Null;
            return globals.TryGetValue(Ludots.Core.Scripting.CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                   localObj is Entity local &&
                   local != Entity.Null &&
                   world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        public static string FormatEntityId(Entity entity) => $"#{entity.Id}";
    }
}
