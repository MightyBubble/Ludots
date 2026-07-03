using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;

namespace ParticipantViewCapabilityMod.Runtime;

public static class ParticipantViewProjection
{
    private static readonly QueryDescription PlayerMemberQuery =
        new QueryDescription().WithAll<PlayerOwner, MapEntity>();

    private static readonly QueryDescription TeamMemberQuery =
        new QueryDescription().WithAll<Team, MapEntity>();

    private static readonly QueryDescription MapMemberQuery =
        new QueryDescription().WithAll<MapEntity>();

    public static Entity[] ResolvePlayerMembers(
        World world,
        MapSession session,
        int playerId)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId), "Player id must be positive.");
        }

        Entity playerRepresentative = session.PlayerEntityLookup.Get(playerId);
        var members = new List<Entity>();
        MapId mapId = session.MapId;
        world.Query(in PlayerMemberQuery, (Entity entity, ref PlayerOwner owner, ref MapEntity mapEntity) =>
        {
            if (owner.PlayerId == playerId &&
                mapEntity.MapId == mapId &&
                entity != playerRepresentative &&
                !world.Has<PlayerIdentity>(entity))
            {
                members.Add(entity);
            }
        });

        SortByEntityId(members);
        return members.ToArray();
    }

    public static Entity[] ResolveTeamMembers(
        World world,
        MapSession session,
        int teamId)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (teamId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(teamId), "Team id must be positive.");
        }

        Entity teamRepresentative = session.TeamEntityLookup.Get(teamId);
        var members = new List<Entity>();
        MapId mapId = session.MapId;
        world.Query(in TeamMemberQuery, (Entity entity, ref Team team, ref MapEntity mapEntity) =>
        {
            if (team.Id == teamId &&
                mapEntity.MapId == mapId &&
                entity != teamRepresentative &&
                !world.Has<TeamIdentity>(entity) &&
                !world.Has<PlayerIdentity>(entity))
            {
                members.Add(entity);
            }
        });

        SortByEntityId(members);
        return members.ToArray();
    }

    public static Entity[] ResolveMapMembers(
        World world,
        MapSession session)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var members = new List<Entity>();
        MapId mapId = session.MapId;
        world.Query(in MapMemberQuery, (Entity entity, ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId == mapId &&
                !world.Has<TeamIdentity>(entity) &&
                !world.Has<PlayerIdentity>(entity) &&
                (world.Has<Team>(entity) || world.Has<PlayerOwner>(entity)))
            {
                members.Add(entity);
            }
        });

        SortByEntityId(members);
        return members.ToArray();
    }

    public static ParticipantKnowledgeSnapshot ResolveKnowledgeSnapshot(
        World world,
        KnowledgeProjectionResolver? resolver,
        Entity viewer,
        Entity target,
        int currentTick)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (resolver == null ||
            viewer == Entity.Null ||
            target == Entity.Null ||
            !world.IsAlive(viewer) ||
            !world.IsAlive(target))
        {
            return ParticipantKnowledgeSnapshot.Unknown(target);
        }

        Span<Entity> scopeMembers = stackalloc Entity[1];
        Span<Entity> relationSources = stackalloc Entity[32];
        Span<Entity> relationTargets = stackalloc Entity[64];
        ScopeKey viewerScope = ScopeKey.Self;
        var roleContext = new RoleResolverContext(
            actor: viewer,
            subject: viewer,
            viewer: viewer);
        if (!resolver.TryResolveWithRelationGrants(
                viewer,
                target,
                currentTick,
                in viewerScope,
                in roleContext,
                scopeMembers,
                relationSources,
                relationTargets,
                out KnowledgeProjection projection))
        {
            return ParticipantKnowledgeSnapshot.Unknown(target);
        }

        return ParticipantKnowledgeSnapshot.FromProjection(in projection);
    }

    private static void SortByEntityId(List<Entity> members)
    {
        members.Sort(static (left, right) =>
        {
            int worldComparison = left.WorldId.CompareTo(right.WorldId);
            return worldComparison != 0
                ? worldComparison
                : left.Id.CompareTo(right.Id);
        });
    }
}

public readonly record struct ParticipantKnowledgeSnapshot(
    Entity Target,
    Entity Source,
    KnowledgePresence Presence,
    KnowledgePositionAccess Position,
    KnowledgeIdMask256 AttributeMask,
    KnowledgeIdMask256 RelationshipTypeMask,
    KnowledgeIdMask256 TagMask,
    bool IsDisclosed,
    int ObservedTick,
    int ExpiryTick,
    int ConfidencePermille)
{
    public static ParticipantKnowledgeSnapshot Unknown(Entity target)
    {
        return new ParticipantKnowledgeSnapshot(
            target,
            Entity.Null,
            KnowledgePresence.Unknown,
            KnowledgePositionAccess.None,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            false,
            0,
            0,
            0);
    }

    public static ParticipantKnowledgeSnapshot FromProjection(in KnowledgeProjection projection)
    {
        return new ParticipantKnowledgeSnapshot(
            projection.Target,
            projection.Source,
            projection.Presence,
            projection.Position,
            projection.AttributeMask,
            projection.RelationshipTypeMask,
            projection.TagMask,
            projection.Source != Entity.Null &&
            projection.Source != projection.Viewer &&
            projection.Source != projection.Target,
            projection.ObservedTick,
            projection.ExpiryTick,
            projection.ConfidencePermille);
    }

    public bool IsKnown => Presence != KnowledgePresence.Unknown;

    public bool IsLiveVisible =>
        Presence == KnowledgePresence.LiveVisible &&
        Position == KnowledgePositionAccess.Live;

    public bool IsLastKnown =>
        IsKnown &&
        Position == KnowledgePositionAccess.LastKnown;

    public bool HasFiniteAttributes => !AttributeMask.IsEmpty;

    public bool HasFiniteRelationships => !RelationshipTypeMask.IsEmpty;

    public bool HasFiniteTags => !TagMask.IsEmpty;
}
