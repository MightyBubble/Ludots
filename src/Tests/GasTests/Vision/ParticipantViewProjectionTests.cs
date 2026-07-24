using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using NUnit.Framework;
using ParticipantViewCapabilityMod.Runtime;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class ParticipantViewProjectionTests
{
    [Test]
    public void ResolvePlayerMembers_UsesFocusedMapSessionPlayerMembershipOnly()
    {
        using var world = World.Create();
        MapSession session = CreateSession();

        Entity playerRepresentative = world.Create(
            new MapEntity { MapId = session.MapId },
            new Name { Value = "Azure Alpha" },
            new PlayerIdentity { PlayerId = 1 },
            new PlayerOwner { PlayerId = 1 },
            new Team { Id = 10 });
        session.PlayerEntityLookup.Register(1, playerRepresentative);

        Entity includedOne = world.Create(
            new MapEntity { MapId = session.MapId },
            new PlayerOwner { PlayerId = 1 },
            new Team { Id = 10 });
        Entity includedTwo = world.Create(
            new MapEntity { MapId = session.MapId },
            new PlayerOwner { PlayerId = 1 },
            new Team { Id = 10 });
        _ = world.Create(
            new MapEntity { MapId = session.MapId },
            new PlayerOwner { PlayerId = 2 },
            new Team { Id = 10 });
        _ = world.Create(
            new MapEntity { MapId = new MapId("other_map") },
            new PlayerOwner { PlayerId = 1 },
            new Team { Id = 10 });

        Entity[] members = ParticipantViewProjection.ResolvePlayerMembers(world, session, 1);

        Assert.That(members, Is.EqualTo(new[] { includedOne, includedTwo }));
    }

    [Test]
    public void ResolveTeamMembers_ExcludesParticipantRepresentativesAndOtherMaps()
    {
        using var world = World.Create();
        MapSession session = CreateSession();

        Entity teamRepresentative = world.Create(
            new MapEntity { MapId = session.MapId },
            new Name { Value = "Azure Vanguard" },
            new TeamIdentity { TeamId = 10 });
        Entity playerRepresentative = world.Create(
            new MapEntity { MapId = session.MapId },
            new Name { Value = "Azure Alpha" },
            new PlayerIdentity { PlayerId = 1 },
            new PlayerOwner { PlayerId = 1 },
            new Team { Id = 10 });
        session.TeamEntityLookup.Register(10, teamRepresentative);
        session.PlayerEntityLookup.Register(1, playerRepresentative);

        Entity includedOne = world.Create(
            new MapEntity { MapId = session.MapId },
            new Team { Id = 10 },
            new PlayerOwner { PlayerId = 1 });
        Entity includedTwo = world.Create(
            new MapEntity { MapId = session.MapId },
            new Team { Id = 10 },
            new PlayerOwner { PlayerId = 2 });
        _ = world.Create(
            new MapEntity { MapId = session.MapId },
            new Team { Id = 20 },
            new PlayerOwner { PlayerId = 3 });
        _ = world.Create(
            new MapEntity { MapId = new MapId("other_map") },
            new Team { Id = 10 },
            new PlayerOwner { PlayerId = 4 });

        Entity[] members = ParticipantViewProjection.ResolveTeamMembers(world, session, 10);

        Assert.That(members, Is.EqualTo(new[] { includedOne, includedTwo }));
    }

    private static MapSession CreateSession()
    {
        var map = new MapConfig
        {
            Id = "participant_view_projection_contract",
        };
        return new MapSession(new MapId(map.Id), map)
        {
            TeamEntityLookup = new TeamEntityLookup(),
            PlayerEntityLookup = new PlayerEntityLookup(),
        };
    }
}
