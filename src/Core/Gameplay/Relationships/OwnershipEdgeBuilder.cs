using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Builds control-plane ownership topology (RFC-0065 CTRL-2): ownership edges <c>playerRep → unit</c> for
    /// entities carrying <see cref="PlayerOwner"/>. Shared by map-load participant binding and runtime spawn.
    /// What happens to a previous owner is decided by the ownership type's catalog rules, not here.
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
            RelationshipRuntime relationships,
            int ownsTypeId,
            PlayerEntityLookup players,
            MapId mapId)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(relationships);
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
                if (TryLink(world, relationships, ownsTypeId, players, candidates[i].Entity, candidates[i].PlayerId))
                {
                    linked++;
                }
            }

            return linked;
        }

        /// <summary>
        /// Links one freshly spawned entity to its player rep when it carries <see cref="PlayerOwner"/> and is not
        /// itself a domain rep. Returns true when an ownership edge was ensured.
        /// </summary>
        public static bool TryLinkSpawnedEntity(
            World world,
            RelationshipRuntime relationships,
            int ownsTypeId,
            PlayerEntityLookup players,
            Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(relationships);
            ArgumentNullException.ThrowIfNull(players);
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                return false;
            }

            if (!world.Has<PlayerOwner>(entity) || world.Has<PlayerIdentity>(entity))
            {
                return false;
            }

            return TryLink(world, relationships, ownsTypeId, players, entity, world.Get<PlayerOwner>(entity).PlayerId);
        }

        private static bool TryLink(
            World world,
            RelationshipRuntime relationships,
            int ownsTypeId,
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

            relationships.EnsureLink(rep, entity, ownsTypeId);
            ParticipantIdentityProjector.SyncPlayerOwner(world, entity, relationships, ownsTypeId);
            return true;
        }
    }
}
