using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Builds control-plane ownership topology (RFC-0065 CTRL-2): <c>Owns(playerRep → unit)</c> edges for
    /// entities carrying <see cref="PlayerOwner"/>. Shared by map-load participant binding and runtime spawn.
    /// Single-direct-owner semantics follow <see cref="OwnershipResolver.EnsureOwnership"/>: re-linking an
    /// owned entity under a different rep removes the previous owns edge first (fail-fast against multi-owner
    /// states is enforced inside <see cref="OwnershipResolver"/>).
    /// An entity whose <see cref="PlayerOwner.PlayerId"/> has no bound rep has no control domain; that is a
    /// topology fact, not an error, so no edge is created for it.
    /// </summary>
    public static class OwnershipEdgeBuilder
    {
        private static readonly QueryDescription OwnedMapEntityQuery = new QueryDescription()
            .WithAll<PlayerOwner, MapEntity>()
            .WithNone<PlayerIdentity>();

        /// <summary>
        /// Links every non-rep entity of the given map that carries <see cref="PlayerOwner"/> to its player rep.
        /// Candidates are collected first because linking mutates relationship components on the iterated entities.
        /// Returns the number of edges ensured.
        /// </summary>
        public static int LinkMapOwnedEntities(
            World world,
            OwnershipResolver ownership,
            PlayerEntityLookup players,
            MapId mapId)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(ownership);
            ArgumentNullException.ThrowIfNull(players);

            var candidates = new List<(Entity Entity, int PlayerId)>(capacity: 64);
            world.Query(in OwnedMapEntityQuery, (Entity entity, ref PlayerOwner owner, ref MapEntity mapEntity) =>
            {
                if (mapEntity.MapId == mapId)
                {
                    candidates.Add((entity, owner.PlayerId));
                }
            });

            int linked = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (TryLink(world, ownership, players, candidates[i].Entity, candidates[i].PlayerId))
                {
                    linked++;
                }
            }

            return linked;
        }

        /// <summary>
        /// Links one freshly spawned entity to its player rep when it carries <see cref="PlayerOwner"/> and is not
        /// itself a domain rep. Returns true when an owns edge was ensured.
        /// </summary>
        public static bool TryLinkSpawnedEntity(
            World world,
            OwnershipResolver ownership,
            PlayerEntityLookup players,
            Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(ownership);
            ArgumentNullException.ThrowIfNull(players);
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                return false;
            }

            if (!world.Has<PlayerOwner>(entity) || world.Has<PlayerIdentity>(entity))
            {
                return false;
            }

            return TryLink(world, ownership, players, entity, world.Get<PlayerOwner>(entity).PlayerId);
        }

        private static bool TryLink(
            World world,
            OwnershipResolver ownership,
            PlayerEntityLookup players,
            Entity entity,
            int playerId)
        {
            if (playerId <= 0)
            {
                return false;
            }

            if (!players.TryGet(playerId, out Entity rep) || rep == Entity.Null || rep == entity || !world.IsAlive(rep))
            {
                return false;
            }

            ownership.EnsureOwnership(rep, entity);
            return true;
        }
    }
}
