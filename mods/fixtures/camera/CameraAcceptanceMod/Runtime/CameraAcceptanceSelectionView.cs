using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;

namespace CameraAcceptanceMod.Runtime
{
    internal static class CameraAcceptanceSelectionView
    {
        public static int CopySelectedEntities(World world, Dictionary<string, object> globals, Span<Entity> destination)
        {
            return TryResolveLocalCommandSourceOwner(world, globals, out Entity owner)
                ? EntityCollectionContextRuntime.Copy(globals, owner, InputCollectionKeyDeclarations.RequireRole(globals, "commandSource"), destination)
                : 0;
        }

        public static Entity[] SnapshotSelectedEntities(World world, Dictionary<string, object> globals)
        {
            return TryResolveLocalCommandSourceOwner(world, globals, out Entity owner)
                ? EntityCollectionContextRuntime.Snapshot(globals, owner, InputCollectionKeyDeclarations.RequireRole(globals, "commandSource"))
                : Array.Empty<Entity>();
        }

        private static bool TryResolveLocalCommandSourceOwner(World world, Dictionary<string, object> globals, out Entity owner)
        {
            owner = Entity.Null;
            return ClientLocalSeatAccess.TryGetSolePossessedRep(globals, out owner) &&
                   owner != Entity.Null &&
                   world.IsAlive(owner);
        }

        public static string FormatEntityId(Entity entity) => $"#{entity.Id}";
    }
}
