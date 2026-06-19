using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;

namespace ParticipantViewCapabilityMod.Runtime;

public static class ParticipantViewProjection
{
    private static readonly QueryDescription PlayerMemberQuery =
        new QueryDescription().WithAll<PlayerOwner, MapEntity>();

    private static readonly QueryDescription TeamMemberQuery =
        new QueryDescription().WithAll<Team, MapEntity>();

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
