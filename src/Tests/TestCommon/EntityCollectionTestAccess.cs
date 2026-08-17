using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace Ludots.Tests
{
    internal static class EntityCollectionTestAccess
    {
        public static Entity[] SnapshotCommandSource(GameEngine engine)
        {
            return TryResolveLocalCommandSourceOwner(engine, out Entity owner)
                ? EntityCollectionContextRuntime.Snapshot(engine.GlobalContext, owner, EntityCollectionKeys.CommandSource)
                : System.Array.Empty<Entity>();
        }

        public static int GetCommandSourceCount(GameEngine engine)
        {
            return TryResolveLocalCommandSourceOwner(engine, out Entity owner)
                ? EntityCollectionContextRuntime.GetCount(engine.GlobalContext, owner, EntityCollectionKeys.CommandSource)
                : 0;
        }

        public static bool TryGetCommandSourcePrimary(GameEngine engine, out Entity primary)
        {
            primary = Entity.Null;
            return TryResolveLocalCommandSourceOwner(engine, out Entity owner) &&
                   EntityCollectionContextRuntime.TryGetPrimary(
                       engine.World,
                       engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       out primary);
        }

        public static bool TryDescribeCommandSourceView(GameEngine engine, out EntityCollectionView view)
        {
            view = default;
            return TryResolveLocalCommandSourceOwner(engine, out Entity owner) &&
                   engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore collections) &&
                   EntityCollectionContextRuntime.TryDescribeView(
                       collections,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       out view);
        }

        public static bool TryGetHoveredEntity(GameEngine engine, out Entity hovered)
        {
            hovered = Entity.Null;
            return TryResolveLocalCommandSourceOwner(engine, out Entity owner) &&
                   EntityCollectionContextRuntime.TryGetPrimary(
                       engine.World,
                       engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.HoveredEntity,
                       out hovered);
        }

        public static bool TryGetHoveredEntity(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            out Entity hovered)
        {
            return EntityCollectionContextRuntime.TryGetPrimary(
                world,
                globals,
                owner,
                EntityCollectionKeys.HoveredEntity,
                out hovered);
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (local == Entity.Null || !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }
    }
}
