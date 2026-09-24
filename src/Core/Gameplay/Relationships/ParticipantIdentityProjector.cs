using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Gameplay.Relationships
{
    public static class ParticipantIdentityProjector
    {
        public static void SyncPlayerOwner(World world, Entity entity, OwnershipResolver ownership)
        {
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                return;
            }

            if (world.Has<PlayerIdentity>(entity))
            {
                UpsertPlayerOwner(world, entity, world.Get<PlayerIdentity>(entity).PlayerId);
                return;
            }

            if (ownership.TryResolveRootOwner(entity, out Entity root) &&
                world.IsAlive(root) &&
                world.Has<PlayerIdentity>(root))
            {
                UpsertPlayerOwner(world, entity, world.Get<PlayerIdentity>(root).PlayerId);
                return;
            }

            if (world.Has<PlayerOwner>(entity))
            {
                world.Remove<PlayerOwner>(entity);
            }
        }

        public static bool TryFindTeamRepresentative(
            World world,
            RelationshipRuntime relationships,
            int memberOfTypeId,
            Entity member,
            out Entity teamRepresentative)
        {
            teamRepresentative = Entity.Null;
            if (member == Entity.Null || !world.IsAlive(member) || memberOfTypeId < 0)
            {
                return false;
            }

            Span<Entity> buffer = stackalloc Entity[8];
            int count = relationships.CollectOutgoing(member, memberOfTypeId, buffer, out int dropped);
            if (dropped > 0)
            {
                var grown = new Entity[count + dropped];
                count = relationships.CollectOutgoing(member, memberOfTypeId, grown, out dropped);
                if (dropped > 0)
                {
                    throw new InvalidOperationException(
                        $"Entity {member.Id} has more MemberOf edges than the projection buffer can read.");
                }

                return SelectTeamRepresentative(world, member, grown.AsSpan(0, count), out teamRepresentative);
            }

            return SelectTeamRepresentative(world, member, buffer.Slice(0, count), out teamRepresentative);
        }

        public static void SyncTeam(World world, Entity member, RelationshipRuntime relationships, int memberOfTypeId)
        {
            if (member == Entity.Null || !world.IsAlive(member))
            {
                return;
            }

            if (TryFindTeamRepresentative(world, relationships, memberOfTypeId, member, out Entity teamRepresentative))
            {
                ProjectTeam(world, member, teamRepresentative);
                return;
            }

            if (world.Has<TeamIdentity>(member))
            {
                UpsertTeam(world, member, world.Get<TeamIdentity>(member).TeamId);
                return;
            }

            if (world.Has<Team>(member))
            {
                world.Remove<Team>(member);
            }
        }

        public static void ProjectTeam(World world, Entity member, Entity teamRepresentative)
        {
            if (member == Entity.Null || !world.IsAlive(member))
            {
                throw new InvalidOperationException("Team projection requires a live member entity.");
            }

            if (teamRepresentative == Entity.Null || !world.IsAlive(teamRepresentative) || !world.Has<TeamIdentity>(teamRepresentative))
            {
                throw new InvalidOperationException(
                    $"Team projection for entity {member.Id} requires a live team representative with TeamIdentity.");
            }

            UpsertTeam(world, member, world.Get<TeamIdentity>(teamRepresentative).TeamId);
        }

        private static bool SelectTeamRepresentative(
            World world,
            Entity member,
            ReadOnlySpan<Entity> targets,
            out Entity teamRepresentative)
        {
            teamRepresentative = Entity.Null;
            int teamId = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                Entity target = targets[i];
                if (!world.IsAlive(target) || !world.Has<TeamIdentity>(target))
                {
                    continue;
                }

                int candidateTeamId = world.Get<TeamIdentity>(target).TeamId;
                if (teamRepresentative != Entity.Null && candidateTeamId != teamId)
                {
                    throw new InvalidOperationException(
                        $"Entity {member.Id} has MemberOf edges to team {teamId} and team {candidateTeamId}.");
                }

                teamRepresentative = target;
                teamId = candidateTeamId;
            }

            return teamRepresentative != Entity.Null;
        }

        public static void UpsertPlayerOwner(World world, Entity entity, int playerId)
        {
            var owner = new PlayerOwner { PlayerId = playerId };
            if (world.Has<PlayerOwner>(entity))
            {
                world.Set(entity, owner);
            }
            else
            {
                world.Add(entity, owner);
            }
        }

        public static void UpsertTeam(World world, Entity entity, int teamId)
        {
            var team = new Team { Id = teamId };
            if (world.Has<Team>(entity))
            {
                world.Set(entity, team);
            }
            else
            {
                world.Add(entity, team);
            }
        }
    }
}
