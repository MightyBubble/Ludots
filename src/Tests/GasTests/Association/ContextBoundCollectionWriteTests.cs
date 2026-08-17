using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>RFC-0065 CTX-5: context-bound collection writes (raw capture + filter + domain routing).</summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ContextBoundCollectionWriteTests
    {
        private const string DefaultProfileId = "filter.controllable.default";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void CommitCast_StoresRawUnfiltered_AndRoutesOnlyControllablesToCommandSource()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            harness.Ownership.EnsureOwnership(p2Rep, m99);

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m01, m02, m99 }, EntityCollectionSourceKind.UiAcquisition);

            Span<Entity> rows = stackalloc Entity[8];
            Assert.That(harness.Store.TryGet(p1Rep, harness.UiCastRawKeyId, out EntityCollectionHandle rawHandle), Is.True);
            Assert.That(harness.Store.CopyEntities(rawHandle, 0, rows), Is.EqualTo(3), "Raw capture must be unfiltered.");
            Assert.That(rows[..3].ToArray(), Is.EqualTo(new[] { m01, m02, m99 }));

            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p1Handle), Is.True);
            int count = harness.Store.CopyEntities(p1Handle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01, m02 }), "m99 is not controllable without a grant.");
            Assert.That(harness.Store.TryGet(p2Rep, harness.CommandSourceKeyId, out _), Is.False, "No routed write may land in P2's domain.");
        }

        [Test]
        public void CommitCast_WithControlsGrant_RoutesForeignUnitIntoItsOwnDomain()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            harness.Ownership.EnsureOwnership(p2Rep, m99);
            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m01, m02, m99 }, EntityCollectionSourceKind.UiAcquisition);

            Span<Entity> rows = stackalloc Entity[8];
            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p1Handle), Is.True);
            int count = harness.Store.CopyEntities(p1Handle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01, m02 }));

            Assert.That(harness.Store.TryGet(p2Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p2Handle), Is.True);
            count = harness.Store.CopyEntities(p2Handle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m99 }), "Granted unit passes the filter and routes to its own domain.");
            Assert.That(harness.Store.TryGetWriterDomainAt(p2Handle, 0, out Entity writerDomain), Is.True);
            Assert.That(writerDomain, Is.EqualTo(p1Rep));
        }

        [Test]
        public void CommitCast_ExcludeTag_DropsEntityFromRoutedWriteButKeepsItInRaw()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            int deadTagId = TagRegistry.Register("state.dead");

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity m01 = world.Create();
            Entity m02 = world.Create(new GameplayTagContainer());
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            world.Get<GameplayTagContainer>(m02).AddTag(deadTagId);

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m01, m02 }, EntityCollectionSourceKind.UiAcquisition);

            Span<Entity> rows = stackalloc Entity[8];
            Assert.That(harness.Store.TryGet(p1Rep, harness.UiCastRawKeyId, out EntityCollectionHandle rawHandle), Is.True);
            Assert.That(harness.Store.CopyEntities(rawHandle, 0, rows), Is.EqualTo(2));

            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle handle), Is.True);
            int count = harness.Store.CopyEntities(handle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01 }), "state.dead must be filtered out of the routed write.");
        }

        [Test]
        public void CommitCast_AbilityFrame_WritesAbilityKeyAndRestoresCommandSourceAfterRemoval()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            Entity m05 = world.Create();
            Entity m06 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            harness.Ownership.EnsureOwnership(p1Rep, m05);
            harness.Ownership.EnsureOwnership(p1Rep, m06);

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m01, m02 }, EntityCollectionSourceKind.UiAcquisition);

            // Ability frame declares no filter profile: explicit 0 = pass-through, no fallback lookup.
            long token = harness.Stack.Push(InteractionContextFrameDescriptor.Create(
                "ctx.ability.test.confirm_targets",
                "collection.ability.test.targets",
                "view.ability.test.targets"));
            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m05, m06 }, EntityCollectionSourceKind.UiAcquisition);

            int abilityKeyId = harness.Store.KeyRegistry.GetId("collection.ability.test.targets");
            Span<Entity> rows = stackalloc Entity[8];
            Assert.That(harness.Store.TryGet(p1Rep, abilityKeyId, out EntityCollectionHandle abilityHandle), Is.True);
            int count = harness.Store.CopyEntities(abilityHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m05, m06 }), "Ability frame casts must land in the ability key.");

            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle commandHandle), Is.True);
            count = harness.Store.CopyEntities(commandHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01, m02 }), "command.source must stay untouched while the ability frame is active.");

            Assert.That(harness.Stack.RemoveByToken(token), Is.True);
            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m01 }, EntityCollectionSourceKind.UiAcquisition);

            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out commandHandle), Is.True);
            count = harness.Store.CopyEntities(commandHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01 }), "Casts must write command.source again after the frame is removed.");
        }

        [Test]
        public void CommitCast_PassThroughFrame_ThrowsWhenRawContainsDomainlessEntity()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity m01 = world.Create();
            Entity neutral = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);

            // Frame without a filter profile: raw hits pass through, so the configurer owns routability.
            // A domainless entity reaching the domain-routed command source must fail loudly (semantic guardrail).
            harness.Stack.Push(InteractionContextFrameDescriptor.Create(
                "ctx.test.pass_through",
                EntityCollectionKeys.CommandSource,
                "view.test.pass_through"));

            Entity[] raw = { m01, neutral };
            Assert.Throws<InvalidOperationException>(
                () => harness.Writer.CommitCast(p1Rep, raw, EntityCollectionSourceKind.UiAcquisition));

            Span<Entity> rows = stackalloc Entity[4];
            Assert.That(harness.Store.TryGet(p1Rep, harness.UiCastRawKeyId, out EntityCollectionHandle rawHandle), Is.True);
            Assert.That(harness.Store.CopyEntities(rawHandle, 0, rows), Is.EqualTo(2), "Raw capture is a client product and still lands.");
            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out _), Is.False, "The rejected routed write must not land.");
        }

        [Test]
        public void CommitCast_SteadyState_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            var raw = new Entity[16];
            for (int i = 0; i < 8; i++)
            {
                Entity own = world.Create();
                Entity foreign = world.Create();
                harness.Ownership.EnsureOwnership(p1Rep, own);
                harness.Ownership.EnsureOwnership(p2Rep, foreign);
                raw[i * 2] = own;
                raw[(i * 2) + 1] = foreign;
            }

            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);
            harness.Writer.CommitCast(p1Rep, raw, EntityCollectionSourceKind.UiAcquisition);

            long allocated = MeasureCommitCastAllocations(harness, p1Rep, raw);
            allocated = Math.Min(allocated, MeasureCommitCastAllocations(harness, p1Rep, raw));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static long MeasureCommitCastAllocations(Harness harness, Entity anchor, Entity[] raw)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Writer.CommitCast(anchor, raw, EntityCollectionSourceKind.UiAcquisition);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public EntityCollectionStore Store = null!;
            public InteractionContextStack Stack = null!;
            public FilterProfileRegistry Filters = null!;
            public ContextBoundCollectionWriter Writer = null!;
            public int ControlsTypeId;
            public int CommandSourceKeyId;
            public int UiCastRawKeyId;

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
                var ownership = new OwnershipResolver(relationships, ownsTypeId);
                var domains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);

                // One key registry shared by the stack and the store, mirroring GameEngine wiring.
                var keyRegistry = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var store = new EntityCollectionStore(keyRegistry, initialCollectionCapacity: 16, initialRowCapacity: 128);
                var stack = new InteractionContextStack(keyRegistry);
                stack.Push(InteractionContextFrameDescriptor.Create(
                    InteractionContextIds.Default,
                    EntityCollectionKeys.CommandSource,
                    "view.test.default",
                    filterProfileId: DefaultProfileId));

                var filters = new FilterProfileRegistry(stack.FilterProfileIdRegistry, world, new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget()));
                filters.RegisterExpander(
                    FilterAssociationExpandKinds.Controls,
                    domains.CollectControlled,
                    () => domains.Revision);
                filters.Install(new FilterProfilesConfig
                {
                    Profiles = new List<FilterProfileDefinition>
                    {
                        new()
                        {
                            Id = DefaultProfileId,
                            AssociationQuery = new FilterProfileAssociationQuery { Anchor = "solePossessedRep", Expand = "controls" },
                            Exclude = new FilterProfileTagRule { AnyTags = new List<string> { "state.dead", "presentation.hidden" } },
                            Include = new FilterProfileTagRule { AnyTags = new List<string>() },
                        },
                    },
                });

                return new Harness
                {
                    Relationships = relationships,
                    Ownership = ownership,
                    Store = store,
                    Stack = stack,
                    Filters = filters,
                    Writer = new ContextBoundCollectionWriter(stack, filters, new DomainRoutedCollectionWriter(store, domains), store),
                    ControlsTypeId = controlsTypeId,
                    CommandSourceKeyId = keyRegistry.Register(EntityCollectionKeys.CommandSource),
                    UiCastRawKeyId = keyRegistry.Register(EntityCollectionKeys.UiCastRaw),
                };
            }
        }
    }
}
