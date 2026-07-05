using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 INT-1/2/3 (DEC-14): CommandIntentProfile registry — explicit total order,
    /// dual-side predicate routing, group routing, and load-time fail-fast. All semantic
    /// names (garrison/weapon/destructible/stances) are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CommandIntentProfileTests
    {
        private const string GarrisonAbilityTag = "ability.catalog.garrison_enter";
        private const string WeaponAbilityTag = "ability.catalog.weapon";
        private const string GarrisonableTag = "structure.garrisonable";
        private const string DestructibleTag = "destructible";
        private const string TestProfileId = "intent.command.test";
        private const int GarrisonAbilityId = 1;
        private const int WeaponAbilityId = 2;

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            ContextGroupIdRegistry.Clear();
        }

        [Test]
        public void TryRoute_DualAttributeTarget_UniqueWinnerByPriorityTotalOrder()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallStandardProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity actor = harness.CreateActor(p1Rep, GarrisonAbilityId, WeaponAbilityId);
            // Target is garrisonable AND destructible; neutral stance satisfies both rule 30 and rule 20.
            Entity target = harness.CreateTaggedEntity(p2Rep, GarrisonableTag, DestructibleTag);

            var facts = new CommandIntentTargetFacts(target, HasEntity: true);
            bool routed = harness.Intents.TryRoute(harness.ProfileId(TestProfileId), actor, p1Rep, in facts, out CommandIntentRoute route);

            Assert.That(routed, Is.True);
            Assert.That(route.RuleIndex, Is.EqualTo(0), "priority 30 (garrison) must be the unique winner over 20.");
            Assert.That(route.OrderTypeId, Is.EqualTo(harness.CastAbilityOrderId));
            Assert.That(route.RouteKind, Is.EqualTo(CommandIntentRouteKinds.ByAbilityTag));
            Assert.That(route.RouteParamId, Is.EqualTo(TagRegistry.GetId(GarrisonAbilityTag)));
        }

        [Test]
        public void Install_DuplicatePriority_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<InvalidOperationException>(() => harness.Intents.Install(Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.bad.priority",
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                    Harness.GroundRule(priority: 10, orderTypeKey: "castAbility"),
                },
            })));
        }

        [Test]
        public void Install_UnknownGroupPolicyKind_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<InvalidOperationException>(() => harness.Intents.Install(Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.bad.policy",
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "bySelector" },
                Rules = new List<CommandIntentRuleDefinition> { Harness.GroundRule(priority: 10, orderTypeKey: "moveTo") },
            })));
        }

        [TestCase("slot0")]
        [TestCase("bySlotIndex:0")]
        public void Install_BareSlotIndexSelector_Throws(string slotSelector)
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            // DEC-14: semantic routing forbids bare slot indices in every spelling.
            Assert.Throws<InvalidOperationException>(() => harness.Intents.Install(Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.bad.slot",
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    new()
                    {
                        Priority = 10,
                        Route = new CommandIntentRouteDefinition { OrderTypeKey = "castAbility", Slot = slotSelector },
                    },
                },
            })));
        }

        [Test]
        public void RouteGroup_MixedCapabilityActors_RoutePerActor()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallStandardProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity garrisonActor = harness.CreateActor(p1Rep, GarrisonAbilityId);
            Entity weaponActor = harness.CreateActor(p1Rep, WeaponAbilityId);
            Entity nakedActor = harness.CreateActor(p1Rep);
            Entity target = harness.CreateTaggedEntity(p2Rep, GarrisonableTag, DestructibleTag);

            var facts = new CommandIntentTargetFacts(target, HasEntity: true);
            Span<Entity> actors = stackalloc Entity[] { garrisonActor, weaponActor, nakedActor };
            Span<CommandIntentRoute> routes = stackalloc CommandIntentRoute[3];
            int routedCount = harness.Intents.RouteGroup(harness.ProfileId(TestProfileId), actors, p1Rep, in facts, routes);

            Assert.That(routedCount, Is.EqualTo(2));
            Assert.That(routes[0].RouteParamId, Is.EqualTo(TagRegistry.GetId(GarrisonAbilityTag)), "garrison-capable actor hits rule 30.");
            Assert.That(routes[1].RouteParamId, Is.EqualTo(TagRegistry.GetId(WeaponAbilityTag)), "weapon-only actor hits rule 20.");
            Assert.That(routes[2].HasRoute, Is.False, "actor without abilities matches no entity-hit rule.");
        }

        [Test]
        public void TryRoute_GroundHit_MatchesOnlyGroundRule()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallStandardProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateActor(p1Rep, GarrisonAbilityId, WeaponAbilityId);

            var groundFacts = new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
            bool routed = harness.Intents.TryRoute(harness.ProfileId(TestProfileId), actor, p1Rep, in groundFacts, out CommandIntentRoute route);

            Assert.That(routed, Is.True);
            Assert.That(route.OrderTypeId, Is.EqualTo(harness.MoveToOrderId));
            Assert.That(route.RouteKind, Is.EqualTo(CommandIntentRouteKinds.None));
        }

        [Test]
        public void TryRoute_StancePredicate_HostileMatchesWeaponRule_FriendlyDoesNot()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallStandardProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity hostileRep = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity friendlyRep = world.Create(new PlayerIdentity { PlayerId = 4 });
            harness.Relationships.EnsureLink(p1Rep, hostileRep, harness.HostileTypeId);
            harness.Relationships.EnsureLink(p1Rep, friendlyRep, harness.FriendlyTypeId);

            Entity actor = harness.CreateActor(p1Rep, WeaponAbilityId);
            Entity hostileTarget = harness.CreateTaggedEntity(hostileRep, DestructibleTag);
            Entity friendlyTarget = harness.CreateTaggedEntity(friendlyRep, DestructibleTag);

            var hostileFacts = new CommandIntentTargetFacts(hostileTarget, HasEntity: true);
            bool hostileRouted = harness.Intents.TryRoute(harness.ProfileId(TestProfileId), actor, p1Rep, in hostileFacts, out CommandIntentRoute hostileRoute);
            Assert.That(hostileRouted, Is.True);
            Assert.That(hostileRoute.RouteParamId, Is.EqualTo(TagRegistry.GetId(WeaponAbilityTag)), "hostile destructible target hits the weapon rule.");

            var friendlyFacts = new CommandIntentTargetFacts(friendlyTarget, HasEntity: true);
            bool friendlyRouted = harness.Intents.TryRoute(harness.ProfileId(TestProfileId), actor, p1Rep, in friendlyFacts, out _);
            Assert.That(friendlyRouted, Is.False, "friendly stance is outside the weapon rule's stance set; no other rule matches.");
        }

        [Test]
        public void TryRoute_WinnerIsFinal_RouteWithUnresolvableAbilityTagStillWins()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.Intents.Install(Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.command.final",
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    new()
                    {
                        Priority = 40,
                        Target = new CommandIntentTargetPredicateDefinition { AnyTags = new List<string> { DestructibleTag } },
                        Route = new CommandIntentRouteDefinition { OrderTypeKey = "castAbility", Slot = "byAbilityTag:ability.catalog.nonexistent" },
                    },
                    new()
                    {
                        Priority = 5,
                        Target = new CommandIntentTargetPredicateDefinition { AnyTags = new List<string> { DestructibleTag } },
                        Route = new CommandIntentRouteDefinition { OrderTypeKey = "moveTo" },
                    },
                },
            }));

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity actor = harness.CreateActor(p1Rep);
            Entity target = harness.CreateTaggedEntity(p2Rep, DestructibleTag);

            var facts = new CommandIntentTargetFacts(target, HasEntity: true);
            bool routed = harness.Intents.TryRoute(harness.ProfileId("intent.command.final"), actor, p1Rep, in facts, out CommandIntentRoute route);

            Assert.That(routed, Is.True);
            Assert.That(route.RuleIndex, Is.EqualTo(0), "winning is final: no fall-through to the lower-priority moveTo rule.");
            Assert.That(route.OrderTypeId, Is.EqualTo(harness.CastAbilityOrderId));
            Assert.That(route.RouteKind, Is.EqualTo(CommandIntentRouteKinds.ByAbilityTag));
            Assert.That(route.RouteParamId, Is.EqualTo(TagRegistry.GetId("ability.catalog.nonexistent")),
                "slot landing is downstream work; the evaluator returns the winning route as-is.");
        }

        [Test]
        public void RouteGroup_SteadyState_IsAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallStandardProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            var actors = new Entity[8];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = harness.CreateActor(p1Rep, (i % 2 == 0) ? GarrisonAbilityId : WeaponAbilityId);
            }

            Entity target = harness.CreateTaggedEntity(p2Rep, GarrisonableTag, DestructibleTag);
            var facts = new CommandIntentTargetFacts(target, HasEntity: true);
            var routes = new CommandIntentRoute[actors.Length];
            int profileId = harness.ProfileId(TestProfileId);

            int warmup = harness.Intents.RouteGroup(profileId, actors, p1Rep, in facts, routes);
            Assert.That(warmup, Is.EqualTo(actors.Length));

            long allocated = MeasureRouteGroupAllocations(harness, profileId, actors, p1Rep, in facts, routes);
            allocated = Math.Min(allocated, MeasureRouteGroupAllocations(harness, profileId, actors, p1Rep, in facts, routes));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state RouteGroup must be allocation free.");
        }

        private static long MeasureRouteGroupAllocations(
            Harness harness,
            int profileId,
            Entity[] actors,
            Entity anchorRep,
            in CommandIntentTargetFacts facts,
            CommandIntentRoute[] routes)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Intents.RouteGroup(profileId, actors, anchorRep, in facts, routes);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        internal sealed class Harness
        {
            public World World = null!;
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public CommandIntentProfileRegistry Intents = null!;
            public StringIntRegistry ProfileIds = null!;
            public int HostileTypeId;
            public int FriendlyTypeId;
            public int CastAbilityOrderId;
            public int MoveToOrderId;

            public static Harness Create(World world)
            {
                var types = new RelationshipTypeRegistry();
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    new RelationshipMetricRegistry(),
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 4),
                    new RelationshipReverseIndex(world));
                int ownsTypeId = types.Register("Owns");
                int controlsTypeId = types.Register("Controls");
                int memberOfTypeId = types.Register("MemberOf");
                int hostileTypeId = types.Register("Hostile", isSymmetric: true);
                int friendlyTypeId = types.Register("Friendly", isSymmetric: true);
                types.Register("Neutral", isSymmetric: true);
                var ownership = new OwnershipResolver(relationships, ownsTypeId);
                var controlDomains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
                var stances = DomainStanceQuery.Create(relationships, memberOfTypeId, new DomainStanceConfig
                {
                    StanceTypes = new List<string> { "Hostile", "Friendly", "Neutral" },
                    SameDomainStance = "Friendly",
                    SameTeamStance = "Friendly",
                    DefaultStance = "Neutral",
                });

                var abilities = new AbilityDefinitionRegistry();
                RegisterAbility(abilities, GarrisonAbilityId, GarrisonAbilityTag);
                RegisterAbility(abilities, WeaponAbilityId, WeaponAbilityTag);

                var orderTypes = new OrderTypeRegistry();
                orderTypes.Register(new OrderTypeConfig { Key = "castAbility", OrderTypeId = 1 });
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });

                var profileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var intents = new CommandIntentProfileRegistry(
                    profileIds,
                    world,
                    new TagOps(new TagRuleRegistry(), new GasBudget()),
                    abilities,
                    controlDomains,
                    stances,
                    orderTypes);
                return new Harness
                {
                    World = world,
                    Relationships = relationships,
                    Ownership = ownership,
                    Intents = intents,
                    ProfileIds = profileIds,
                    HostileTypeId = hostileTypeId,
                    FriendlyTypeId = friendlyTypeId,
                    CastAbilityOrderId = 1,
                    MoveToOrderId = 2,
                };
            }

            public int ProfileId(string name) => ProfileIds.GetId(name);

            public Entity CreateActor(Entity ownerRep, params int[] abilityIds)
            {
                Entity actor = World.Create(new AbilityStateBuffer());
                ref AbilityStateBuffer slots = ref World.Get<AbilityStateBuffer>(actor);
                for (int i = 0; i < abilityIds.Length; i++)
                {
                    slots.AddAbility(abilityIds[i]);
                }

                Ownership.EnsureOwnership(ownerRep, actor);
                return actor;
            }

            public Entity CreateTaggedEntity(Entity ownerRep, params string[] tags)
            {
                Entity entity = World.Create(new GameplayTagContainer());
                ref GameplayTagContainer container = ref World.Get<GameplayTagContainer>(entity);
                for (int i = 0; i < tags.Length; i++)
                {
                    container.AddTag(TagRegistry.Register(tags[i]));
                }

                Ownership.EnsureOwnership(ownerRep, entity);
                return entity;
            }

            /// <summary>The §5.11 exemplar: garrison(30) &gt; weapon(20) &gt; ground move(10).</summary>
            public void InstallStandardProfile()
            {
                Intents.Install(Config(new CommandIntentProfileDefinition
                {
                    Id = TestProfileId,
                    GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                    Rules = new List<CommandIntentRuleDefinition>
                    {
                        new()
                        {
                            Priority = 30,
                            Actor = new CommandIntentActorPredicateDefinition { HasAbilityWithTag = GarrisonAbilityTag },
                            Target = new CommandIntentTargetPredicateDefinition
                            {
                                AllTags = new List<string> { GarrisonableTag },
                                Stance = new List<string> { "Neutral", "Friendly" },
                            },
                            Route = new CommandIntentRouteDefinition { OrderTypeKey = "castAbility", Slot = $"byAbilityTag:{GarrisonAbilityTag}" },
                        },
                        new()
                        {
                            Priority = 20,
                            Actor = new CommandIntentActorPredicateDefinition { HasAbilityWithTag = WeaponAbilityTag },
                            Target = new CommandIntentTargetPredicateDefinition
                            {
                                AnyTags = new List<string> { DestructibleTag },
                                Stance = new List<string> { "Hostile", "Neutral" },
                            },
                            Route = new CommandIntentRouteDefinition { OrderTypeKey = "castAbility", Slot = $"byAbilityTag:{WeaponAbilityTag}" },
                        },
                        new()
                        {
                            Priority = 10,
                            Target = new CommandIntentTargetPredicateDefinition { HasEntity = false },
                            Route = new CommandIntentRouteDefinition { OrderTypeKey = "moveTo" },
                        },
                    },
                }));
            }

            public static CommandIntentRuleDefinition GroundRule(int priority, string orderTypeKey)
            {
                return new CommandIntentRuleDefinition
                {
                    Priority = priority,
                    Target = new CommandIntentTargetPredicateDefinition { HasEntity = false },
                    Route = new CommandIntentRouteDefinition { OrderTypeKey = orderTypeKey },
                };
            }

            public static CommandIntentProfilesConfig Config(params CommandIntentProfileDefinition[] profiles)
            {
                return new CommandIntentProfilesConfig { Profiles = new List<CommandIntentProfileDefinition>(profiles) };
            }

            private static void RegisterAbility(AbilityDefinitionRegistry registry, int abilityId, string catalogTag)
            {
                var def = new AbilityDefinition { HasCatalogTags = true };
                def.CatalogTags.AddTag(TagRegistry.Register(catalogTag));
                registry.Register(abilityId, in def);
            }
        }
    }
}
