using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Systems;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ParticipantBindingContractTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            TeamManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            TeamManager.Clear();
        }

        [Test]
        public void ParticipantBindingResolver_MapOwnedLogicalEntities_WritesIdentityLookupsRelationshipsAndLocalPlayer()
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map)
            {
                LaunchContext = MapLaunchContext.Create(selectedPlayerId: 7),
            };
            var index = CreateEntityIndex(map.Id, world, out Entity teamOne, out Entity teamTwo, out Entity playerOne, out Entity playerTwo);
            var types = new RelationshipTypeRegistry();
            int allianceType = types.Register("Alliance");
            int rivalryType = types.Register("Rivalry");
            int membershipType = types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);

            ParticipantBindingResult result = ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.TeamEntityLookup.Name] = new TeamEntityLookup(),
                [CoreServiceKeys.PlayerEntityLookup.Name] = new PlayerEntityLookup(),
            };
            TeamEntityLookup focusedTeamLookup = (TeamEntityLookup)globals[CoreServiceKeys.TeamEntityLookup.Name];
            PlayerEntityLookup focusedPlayerLookup = (PlayerEntityLookup)globals[CoreServiceKeys.PlayerEntityLookup.Name];

            ParticipantBindingResolver.PublishFocused(globals, result);

            Assert.That(world.Has<WorldPositionCm>(teamOne), Is.False, "Participant representative may be a logical entity without spatial components.");
            Assert.That(world.Get<TeamIdentity>(teamOne).TeamId, Is.EqualTo(10));
            Assert.That(world.Get<TeamIdentity>(teamTwo).TeamId, Is.EqualTo(20));
            Assert.That(world.Get<PlayerIdentity>(playerOne).PlayerId, Is.EqualTo(7));
            Assert.That(world.Get<PlayerIdentity>(playerTwo).PlayerId, Is.EqualTo(8));
            Assert.That(world.Get<PlayerOwner>(playerOne).PlayerId, Is.EqualTo(7));
            Assert.That(world.Get<Team>(playerOne).Id, Is.EqualTo(10));
            Assert.That(world.Get<Team>(playerTwo).Id, Is.EqualTo(20));

            Assert.That(result.Teams.Get(10), Is.EqualTo(teamOne));
            Assert.That(result.Players.Get(7), Is.EqualTo(playerOne));
            Assert.That(result.LocalPlayerId, Is.EqualTo(7));
            Assert.That(result.LocalPlayerEntity, Is.EqualTo(playerOne));
            Assert.That(globals[CoreServiceKeys.LocalPlayerId.Name], Is.EqualTo(7));
            Assert.That(globals[CoreServiceKeys.LocalPlayerEntity.Name], Is.EqualTo(playerOne));
            Assert.That(globals[CoreServiceKeys.TeamEntityLookup.Name], Is.SameAs(focusedTeamLookup));
            Assert.That(globals[CoreServiceKeys.PlayerEntityLookup.Name], Is.SameAs(focusedPlayerLookup));
            Assert.That(focusedTeamLookup.Get(10), Is.EqualTo(teamOne));
            Assert.That(focusedPlayerLookup.Get(7), Is.EqualTo(playerOne));

            Assert.That(relationships.HasLink(teamOne, teamTwo, allianceType), Is.True);
            Assert.That(relationships.HasLink(teamTwo, teamOne, allianceType), Is.True);
            Assert.That(relationships.HasLink(playerOne, playerTwo, rivalryType), Is.True);
            Assert.That(relationships.HasLink(playerTwo, playerOne, rivalryType), Is.False);
            Assert.That(relationships.HasLink(playerOne, teamOne, membershipType), Is.True);
            Assert.That(TeamManager.GetRelationship(10, 20), Is.EqualTo(TeamRelationship.Friendly));

            int commandPower = AttributeRegistry.GetId("CommandPower");
            Assert.That(world.Get<AttributeBuffer>(teamOne).GetCurrent(commandPower), Is.EqualTo(50f));
            int commandTag = TagRegistry.GetId("State.CommandHub");
            Assert.That(world.Get<GameplayTagContainer>(teamOne).HasTag(commandTag), Is.True);
        }

        [TestCase("duplicateTeamId")]
        [TestCase("duplicatePlayerId")]
        [TestCase("duplicateTeamRepresentative")]
        [TestCase("duplicatePlayerRepresentative")]
        [TestCase("missingRepresentative")]
        [TestCase("unknownTeam")]
        [TestCase("blankRelationshipType")]
        public void ParticipantBindingResolver_InvalidAuthoring_FailsExplicitly(string scenario)
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map)
            {
                LaunchContext = MapLaunchContext.Create(selectedPlayerId: 7),
            };
            var index = CreateEntityIndex(map.Id, world, out _, out _, out _, out _);
            var types = new RelationshipTypeRegistry();
            types.Register("Alliance");
            types.Register("Rivalry");
            types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);
            ApplyInvalidScenario(map, scenario);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership))!;

            Assert.That(ex.Message, Is.Not.Empty);
        }

        [Test]
        public void LocalPlayerEntityResolverSystem_UsesLookupAndDoesNotScanPlayerOwner()
        {
            using var world = World.Create();
            Entity strayOwner = world.Create(new PlayerOwner { PlayerId = 7 });
            Entity representative = world.Create(new PlayerIdentity { PlayerId = 7 }, new PlayerOwner { PlayerId = 7 });
            var lookup = new PlayerEntityLookup();
            lookup.Register(7, representative);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerId.Name] = 7,
                [CoreServiceKeys.PlayerEntityLookup.Name] = lookup,
            };
            var system = new LocalPlayerEntityResolverSystem(world, globals);

            system.Update(0f);

            Assert.That(globals[CoreServiceKeys.LocalPlayerEntity.Name], Is.EqualTo(representative));
            Assert.That(globals[CoreServiceKeys.LocalPlayerEntity.Name], Is.Not.EqualTo(strayOwner));
        }

        [Test]
        public void LocalPlayerEntityResolverSystem_MissingLookupLeavesManualExplicitBindingUntouched()
        {
            using var world = World.Create();
            Entity manual = world.Create(new PlayerOwner { PlayerId = 7 });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = manual,
            };
            var system = new LocalPlayerEntityResolverSystem(world, globals);

            system.Update(0f);

            Assert.That(globals[CoreServiceKeys.LocalPlayerEntity.Name], Is.EqualTo(manual));
        }

        [Test]
        public void ParticipantBindingResolver_PublishFocused_RestoresManualLocalPlayerEntityWithoutLocalPlayerId()
        {
            using var world = World.Create();
            Entity manual = world.Create(new PlayerOwner { PlayerId = 7 });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.TeamEntityLookup.Name] = new TeamEntityLookup(),
                [CoreServiceKeys.PlayerEntityLookup.Name] = new PlayerEntityLookup(),
                [CoreServiceKeys.LocalPlayerId.Name] = 9,
                [CoreServiceKeys.LocalPlayerEntity.Name] = world.Create(new PlayerOwner { PlayerId = 9 }),
            };

            var result = new ParticipantBindingResult(
                new TeamEntityLookup(),
                new PlayerEntityLookup(),
                localPlayerId: 0,
                localPlayerEntity: manual);

            ParticipantBindingResolver.PublishFocused(globals, result);

            Assert.That(globals.ContainsKey(CoreServiceKeys.LocalPlayerId.Name), Is.False);
            Assert.That(globals[CoreServiceKeys.LocalPlayerEntity.Name], Is.EqualTo(manual));
        }

        [Test]
        public void LocalPlayerEntityResolverSystem_SelectedPlayerChangeReplacesExistingLocalPlayerEntity()
        {
            using var world = World.Create();
            Entity playerSeven = world.Create(new PlayerIdentity { PlayerId = 7 });
            Entity playerEight = world.Create(new PlayerIdentity { PlayerId = 8 });
            var lookup = new PlayerEntityLookup();
            lookup.Register(7, playerSeven);
            lookup.Register(8, playerEight);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerId.Name] = 7,
                [CoreServiceKeys.PlayerEntityLookup.Name] = lookup,
                [CoreServiceKeys.LocalPlayerEntity.Name] = playerEight,
            };
            var system = new LocalPlayerEntityResolverSystem(world, globals);

            system.Update(0f);

            Assert.That(globals[CoreServiceKeys.LocalPlayerEntity.Name], Is.EqualTo(playerSeven));
        }

        [Test]
        public void ParticipantBindingResolver_BuildsOwnsAndMemberOfEdges_AndControlDomainResolves()
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map)
            {
                LaunchContext = MapLaunchContext.Create(selectedPlayerId: 7),
            };
            var index = CreateEntityIndex(map.Id, world, out Entity teamOne, out Entity teamTwo, out Entity playerOne, out Entity playerTwo);
            var types = new RelationshipTypeRegistry();
            types.Register("Alliance");
            types.Register("Rivalry");
            types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);
            int ownsType = types.GetId("Owns");
            int memberOfType = types.GetId("MemberOf");
            Entity unitOfPlayerOne = world.Create(
                new PlayerOwner { PlayerId = 7 },
                new MapEntity { MapId = new MapId(map.Id) });
            Entity unitOfPlayerTwo = world.Create(
                new PlayerOwner { PlayerId = 8 },
                new MapEntity { MapId = new MapId(map.Id) });
            Entity unitOfOtherMap = world.Create(
                new PlayerOwner { PlayerId = 7 },
                new MapEntity { MapId = new MapId("some_other_map") });

            ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership);

            Assert.That(relationships.HasLink(playerOne, unitOfPlayerOne, ownsType), Is.True);
            Assert.That(relationships.HasLink(playerTwo, unitOfPlayerTwo, ownsType), Is.True);
            Assert.That(relationships.HasLink(playerOne, unitOfOtherMap, ownsType), Is.False, "Owns edges are scoped to the bound map.");
            Assert.That(relationships.HasLink(playerOne, teamOne, memberOfType), Is.True);
            Assert.That(relationships.HasLink(playerTwo, teamTwo, memberOfType), Is.True);
            Assert.That(relationships.HasLink(playerOne, playerOne, ownsType), Is.False, "Reps never own themselves.");

            var controlDomains = new ControlDomainQuery(world, relationships, ownership, ownsType, types.GetId("Controls"));
            Assert.That(controlDomains.TryResolveControlDomain(unitOfPlayerOne, out Entity domainOne), Is.True);
            Assert.That(domainOne, Is.EqualTo(playerOne));
            Assert.That(controlDomains.TryResolveControlDomain(unitOfPlayerTwo, out Entity domainTwo), Is.True);
            Assert.That(domainTwo, Is.EqualTo(playerTwo));
        }

        [Test]
        public void ParticipantBindingResolver_RebindingOwnedUnitToAnotherPlayer_KeepsSingleDirectOwner()
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map);
            var index = CreateEntityIndex(map.Id, world, out _, out _, out Entity playerOne, out Entity playerTwo);
            var types = new RelationshipTypeRegistry();
            types.Register("Alliance");
            types.Register("Rivalry");
            types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);
            int ownsType = types.GetId("Owns");
            Entity unit = world.Create(
                new PlayerOwner { PlayerId = 7 },
                new MapEntity { MapId = new MapId(map.Id) });

            ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership);
            Assert.That(relationships.HasLink(playerOne, unit, ownsType), Is.True);

            world.Set(unit, new PlayerOwner { PlayerId = 8 });
            OwnershipEdgeBuilder.LinkMapOwnedEntities(world, ownership, RebuildPlayerLookup(playerOne, playerTwo), session.MapId);

            Assert.That(relationships.HasLink(playerOne, unit, ownsType), Is.False, "Single direct owner: the previous owns edge must be removed.");
            Assert.That(relationships.HasLink(playerTwo, unit, ownsType), Is.True);
            Assert.That(ownership.TryGetDirectOwner(unit, out Entity owner), Is.True);
            Assert.That(owner, Is.EqualTo(playerTwo));
        }

        private static PlayerEntityLookup RebuildPlayerLookup(Entity playerOne, Entity playerTwo)
        {
            var lookup = new PlayerEntityLookup();
            lookup.Register(7, playerOne);
            lookup.Register(8, playerTwo);
            return lookup;
        }

        [Test]
        public void ParticipantBindingResolver_LaunchContextSelectedPlayerId_SelectsLocalPlayer()
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map)
            {
                LaunchContext = MapLaunchContext.Create(selectedPlayerId: 8),
            };
            var index = CreateEntityIndex(map.Id, world, out _, out _, out _, out Entity playerTwo);
            var types = new RelationshipTypeRegistry();
            types.Register("Alliance");
            types.Register("Rivalry");
            types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);

            ParticipantBindingResult result = ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership);

            Assert.That(result.LocalPlayerId, Is.EqualTo(8));
            Assert.That(result.LocalPlayerEntity, Is.EqualTo(playerTwo));
        }

        [Test]
        public void ParticipantBindingResolver_WithoutLaunchContext_HasNoLocalPlayer()
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map);
            var index = CreateEntityIndex(map.Id, world, out _, out _, out _, out _);
            var types = new RelationshipTypeRegistry();
            types.Register("Alliance");
            types.Register("Rivalry");
            types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);

            ParticipantBindingResult result = ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership);

            Assert.That(result.LocalPlayerId, Is.EqualTo(0));
            Assert.That(result.LocalPlayerEntity, Is.EqualTo(Entity.Null));
        }

        [Test]
        public void ParticipantBindingResolver_LaunchContextUnknownSelectedPlayerId_FailsExplicitly()
        {
            using var world = World.Create();
            var map = CreateMap();
            var session = new MapSession(new MapId(map.Id), map)
            {
                LaunchContext = MapLaunchContext.Create(selectedPlayerId: 99),
            };
            var index = CreateEntityIndex(map.Id, world, out _, out _, out _, out _);
            var types = new RelationshipTypeRegistry();
            types.Register("Alliance");
            types.Register("Rivalry");
            types.Register("Membership");
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, types);
            OwnershipResolver ownership = CreateOwnership(relationships, types);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ParticipantBindingResolver.Resolve(session, world, index, relationships, types, ownership))!;

            Assert.That(ex.Message, Does.Contain("SelectedPlayerId 99"));
        }

        private static MapConfig CreateMap()
        {
            return new MapConfig
            {
                Id = "participant_contract",
                Teams =
                {
                    new TeamBindingData { TeamId = 10, RepresentativeInstanceId = "team.alpha" },
                    new TeamBindingData { TeamId = 20, RepresentativeInstanceId = "team.beta" },
                },
                Players =
                {
                    new PlayerBindingData { PlayerId = 7, TeamId = 10, RepresentativeInstanceId = "player.local" },
                    new PlayerBindingData { PlayerId = 8, TeamId = 20, RepresentativeInstanceId = "player.remote" },
                },
                ParticipantRelationships = new ParticipantRelationshipConfig
                {
                    Teams =
                    {
                        new TeamRelationshipBindingData
                        {
                            TeamA = 10,
                            TeamB = 20,
                            TypeId = "Alliance",
                            Attitude = "Friendly",
                            Symmetric = true,
                        },
                    },
                    Players =
                    {
                        new PlayerRelationshipBindingData
                        {
                            PlayerA = 7,
                            PlayerB = 8,
                            TypeId = "Rivalry",
                            Symmetric = false,
                        },
                    },
                    PlayerTeams =
                    {
                        new PlayerTeamRelationshipBindingData
                        {
                            PlayerId = 7,
                            TeamId = 10,
                            TypeId = "Membership",
                            Symmetric = false,
                        },
                    },
                },
            };
        }

        private static MapLoadEntityIndex CreateEntityIndex(
            string mapId,
            World world,
            out Entity teamOne,
            out Entity teamTwo,
            out Entity playerOne,
            out Entity playerTwo)
        {
            int commandPower = AttributeRegistry.Register("CommandPower");
            int commandTag = TagRegistry.Register("State.CommandHub");
            teamOne = world.Create(CreateAttributes(commandPower, 50f), CreateTags(commandTag), new TagCountContainer());
            teamTwo = world.Create();
            playerOne = world.Create();
            playerTwo = world.Create();
            var index = new MapLoadEntityIndex();
            index.Register(mapId, "team.alpha", teamOne);
            index.Register(mapId, "team.beta", teamTwo);
            index.Register(mapId, "player.local", playerOne);
            index.Register(mapId, "player.remote", playerTwo);
            return index;
        }

        private static OwnershipResolver CreateOwnership(RelationshipRuntime relationships, RelationshipTypeRegistry types)
        {
            int ownsType = types.Register("Owns");
            types.Register("Controls");
            types.Register("MemberOf");
            return new OwnershipResolver(relationships, ownsType);
        }

        private static RelationshipRuntime CreateRelationshipRuntime(World world, RelationshipTypeRegistry types)
        {
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                new RelationshipReverseIndex(world));
        }

        private static AttributeBuffer CreateAttributes(int attributeId, float value)
        {
            var attributes = new AttributeBuffer();
            attributes.SetBase(attributeId, value);
            return attributes;
        }

        private static GameplayTagContainer CreateTags(int tagId)
        {
            var tags = new GameplayTagContainer();
            tags.AddTag(tagId);
            return tags;
        }

        private static void ApplyInvalidScenario(MapConfig map, string scenario)
        {
            switch (scenario)
            {
                case "duplicateTeamId":
                    map.Teams.Add(new TeamBindingData { TeamId = 10, RepresentativeInstanceId = "player.local" });
                    return;
                case "duplicatePlayerId":
                    map.Players.Add(new PlayerBindingData { PlayerId = 7, TeamId = 10, RepresentativeInstanceId = "team.alpha" });
                    return;
                case "duplicateTeamRepresentative":
                    map.Teams.Add(new TeamBindingData { TeamId = 30, RepresentativeInstanceId = "team.alpha" });
                    return;
                case "duplicatePlayerRepresentative":
                    map.Players.Add(new PlayerBindingData { PlayerId = 9, TeamId = 10, RepresentativeInstanceId = "player.local" });
                    return;
                case "missingRepresentative":
                    map.Players[0].RepresentativeInstanceId = "missing.player";
                    return;
                case "unknownTeam":
                    map.Players[0].TeamId = 999;
                    return;
                case "blankRelationshipType":
                    map.ParticipantRelationships.Teams[0].TypeId = string.Empty;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }
    }
}
