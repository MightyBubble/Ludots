using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RelationshipCoreTests
    {
        [Test]
        public void RelationshipRuntime_SupportsMultipleDirectedTypedEdgesBetweenSamePair()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var bands = new RelationshipBandRegistry();
            var changes = new RelationshipChangeBuffer(capacity: 1);
            var runtime = new RelationshipRuntime(world, types, metrics, flags, bands, changes);

            int socialBondTypeId = types.Register("SocialBond");
            int hostilityTypeId = types.Register("Hostility");
            int loyaltyId = metrics.Register("Loyalty", minValue: -100, maxValue: 100, defaultValue: 10);
            int threatId = metrics.Register("Threat", minValue: 0, maxValue: 200, defaultValue: 0);

            Entity source = world.Create();
            Entity target = world.Create();

            Assert.That(runtime.SetMetric(source, target, socialBondTypeId, loyaltyId, 25, reasonId: 0), Is.EqualTo(25));
            Assert.That(runtime.SetMetric(source, target, hostilityTypeId, threatId, 70, reasonId: 0), Is.EqualTo(70));

            Assert.That(runtime.GetMetric(source, target, socialBondTypeId, loyaltyId), Is.EqualTo(25));
            Assert.That(runtime.GetMetric(source, target, hostilityTypeId, threatId), Is.EqualTo(70));
            Assert.That(runtime.GetMetric(source, target, socialBondTypeId, threatId), Is.EqualTo(0));

            runtime.RemoveLink(source, target, hostilityTypeId);

            Assert.That(runtime.HasLink(source, target, hostilityTypeId), Is.False);
            Assert.That(runtime.HasLink(source, target, socialBondTypeId), Is.True);
            Assert.That(runtime.GetMetric(source, target, socialBondTypeId, loyaltyId), Is.EqualTo(25));
        }

        [Test]
        public void RelationshipRuntime_MaterializesIndexedRelationshipEntity()
        {
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            int socialBondTypeId = types.Register("Tests.Relationship.SocialBond");
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, socialBondTypeId);

            Assert.That(runtime.TryResolveRelationshipEntity(source, target, socialBondTypeId, out Entity relationEntity), Is.True);
            Assert.That(relationEntity, Is.Not.EqualTo(Entity.Null));
            Assert.That(world.Has<RelationshipInstanceCm>(relationEntity), Is.True);
            Assert.That(world.Has<AttributeBuffer>(relationEntity), Is.True);
            Assert.That(world.Has<GameplayTagContainer>(relationEntity), Is.True);
            Assert.That(world.Has<TagCountContainer>(relationEntity), Is.True);
            Assert.That(world.Has<ActiveEffectContainer>(relationEntity), Is.True);

            ref readonly RelationshipInstanceCm instance = ref world.Get<RelationshipInstanceCm>(relationEntity);
            Assert.That(instance.Source, Is.EqualTo(source));
            Assert.That(instance.Target, Is.EqualTo(target));
            Assert.That(instance.TypeId, Is.EqualTo(socialBondTypeId));
            Assert.That(instance.Revision, Is.EqualTo(1));
        }

        [Test]
        public void RelationshipEntityAttributesReceiveGasBuffs()
        {
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            int socialBondTypeId = types.Register("Tests.Relationship.SocialBondBuff");
            int pressureId = EnsureAttribute("Tests.Relationship.Pressure");
            int effectTagId = EnsureTag("Effect.Test.RelationshipPressureBuff");
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, socialBondTypeId);
            Assert.That(runtime.TryResolveRelationshipEntity(source, target, socialBondTypeId, out Entity relationEntity), Is.True);
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(relationEntity);
            attributes.SetBase(pressureId, 1f);

            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(pressureId, ModifierOp.Add, 2f);
            templates.Register(2301, new EffectTemplateData
            {
                TagId = effectTagId,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 10,
                PeriodTicks = 0,
                Modifiers = modifiers,
            });

            var requests = new EffectRequestQueue();
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types);
            var application = new EffectApplicationSystem(world, requests, templates: templates);
            var aggregator = new AttributeAggregatorSystem(world);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = Entity.Null,
                Target = relationEntity,
                TargetContext = Entity.Null,
                TemplateId = 2301,
            });

            proposal.Update(0f);
            application.Update(0f);
            aggregator.Update(0f);

            Assert.That(world.Get<AttributeBuffer>(relationEntity).GetCurrent(pressureId), Is.EqualTo(3f));
        }

        [Test]
        public void RelationshipRuntime_RemoveLinkDestroysMaterializedEntity()
        {
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            int socialBondTypeId = types.Register("Tests.Relationship.Remove");
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, socialBondTypeId);
            Assert.That(runtime.TryResolveRelationshipEntity(source, target, socialBondTypeId, out Entity relationEntity), Is.True);

            runtime.RemoveLink(source, target, socialBondTypeId);

            Assert.That(runtime.TryResolveRelationshipEntity(source, target, socialBondTypeId, out _), Is.False);
            Assert.That(world.IsAlive(relationEntity), Is.False);
        }

        [Test]
        public void RelationshipRuntime_RebuildIndexFailsFastForDuplicateProjection()
        {
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            int socialBondTypeId = types.Register("Tests.Relationship.DuplicateProjection");
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, socialBondTypeId);
            world.Create(new RelationshipInstanceCm
            {
                Source = source,
                Target = target,
                TypeId = socialBondTypeId,
            });

            Assert.Throws<InvalidOperationException>(() => runtime.RebuildEntityIndexFromWorld());
        }

        [Test]
        public void RelationshipRuntime_CollectsTypedOutgoingIncomingAndBetweenPair()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var bands = new RelationshipBandRegistry();
            var changes = new RelationshipChangeBuffer(capacity: 1);
            var runtime = new RelationshipRuntime(world, types, metrics, flags, bands, changes);

            int socialBondTypeId = types.Register("SocialBond");
            int hostilityTypeId = types.Register("Hostility");

            Entity a = world.Create();
            Entity b = world.Create();
            Entity c = world.Create();
            Entity d = world.Create();

            runtime.EnsureLink(a, b, socialBondTypeId);
            runtime.EnsureLink(a, c, socialBondTypeId);
            runtime.EnsureLink(d, a, socialBondTypeId);
            runtime.EnsureLink(a, b, hostilityTypeId);
            runtime.EnsureLink(b, a, hostilityTypeId);

            Span<Entity> buffer = stackalloc Entity[8];

            int outgoingSocialCount = runtime.CollectOutgoing(a, socialBondTypeId, buffer);
            Assert.That(outgoingSocialCount, Is.EqualTo(2));
            Assert.That(buffer[..outgoingSocialCount].ToArray(), Does.Contain(b));
            Assert.That(buffer[..outgoingSocialCount].ToArray(), Does.Contain(c));

            int incomingSocialCount = runtime.CollectIncoming(a, socialBondTypeId, buffer);
            Assert.That(incomingSocialCount, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(d));

            int betweenHostilityCount = runtime.CollectBetweenPair(a, b, hostilityTypeId, buffer);
            Assert.That(betweenHostilityCount, Is.EqualTo(2));
            Assert.That(buffer[..betweenHostilityCount].ToArray(), Does.Contain(a));
            Assert.That(buffer[..betweenHostilityCount].ToArray(), Does.Contain(b));
        }

        [Test]
        public void RelationshipCallbackProcessor_FiltersCallbacksByRelationshipType()
        {
            using var world = World.Create();
            var tagOps = new TagOps(new TagRuleRegistry(), new GasBudget());
            var teamLookup = new TeamEntityLookup();
            var processor = new RelationshipCallbackProcessor(world, tagOps, teamLookup);
            var runtime = new RelationshipCatalogRuntime();
            Entity source = world.Create();
            Entity target = world.Create(new GameplayTagContainer(), new TagCountContainer());
            int trustedTagId = TagRegistry.Register("Tests.Relationship.Trusted");

            runtime.Callbacks.Add(new RelationshipCallbackRule(
                id: "Trusted",
                typeId: 1,
                metricId: 0,
                minimumValue: 60,
                maximumValue: null,
                enterEventKey: new EventKey(string.Empty),
                exitEventKey: new EventKey(string.Empty),
                addTagsToSource: Array.Empty<int>(),
                addTagsToTarget: new[] { trustedTagId },
                addTagsToSourceTeam: Array.Empty<int>(),
                addTagsToTargetTeam: Array.Empty<int>(),
                removeTagsFromSource: Array.Empty<int>(),
                removeTagsFromTarget: new[] { trustedTagId },
                removeTagsFromSourceTeam: Array.Empty<int>(),
                removeTagsFromTargetTeam: Array.Empty<int>()));

            var wrongTypeEnter = new RelationshipChangeRecord(source, target, typeId: 2, metricId: 0, reasonId: 0, oldValue: 50, newValue: 65, oldFlags: 0, newFlags: 0);
            processor.Process(new GameEngine(), runtime, new[] { wrongTypeEnter });
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(trustedTagId), Is.False);

            var matchingTypeEnter = new RelationshipChangeRecord(source, target, typeId: 1, metricId: 0, reasonId: 0, oldValue: 50, newValue: 65, oldFlags: 0, newFlags: 0);
            processor.Process(new GameEngine(), runtime, new[] { matchingTypeEnter });
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(trustedTagId), Is.True);
        }

        [Test]
        public void RelationshipChangeBuffer_GrowsInsteadOfDroppingRecords()
        {
            var buffer = new RelationshipChangeBuffer(capacity: 1);
            Assert.That(buffer.TryAdd(default), Is.True);
            Assert.That(buffer.TryAdd(default), Is.True);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.ResizeCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(2));
        }

        private static RelationshipRuntime CreateRuntime(World world, out RelationshipTypeRegistry types)
        {
            types = new RelationshipTypeRegistry();
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer());
        }

        private static int EnsureAttribute(string attribute)
        {
            int id = AttributeRegistry.GetId(attribute);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(attribute);
        }

        private static int EnsureTag(string tag)
        {
            int id = TagRegistry.GetId(tag);
            return id != TagRegistry.InvalidId ? id : TagRegistry.Register(tag);
        }
    }
}
