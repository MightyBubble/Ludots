using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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
            var runtime = new RelationshipRuntime(world, types, metrics, flags, bands, changes, new RelationshipReverseIndex(world));

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
        public void RelationshipRuntime_CollectsTypedOutgoingIncomingAndBetweenPair()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var bands = new RelationshipBandRegistry();
            var changes = new RelationshipChangeBuffer(capacity: 1);
            var runtime = new RelationshipRuntime(world, types, metrics, flags, bands, changes, new RelationshipReverseIndex(world));

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
        public void RelationshipRuntime_TypedEdgeChurnOnExistingPair_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var bands = new RelationshipBandRegistry();
            var changes = new RelationshipChangeBuffer(capacity: 4_096);
            var runtime = new RelationshipRuntime(world, types, metrics, flags, bands, changes, new RelationshipReverseIndex(world));

            int allyTypeId = types.Register("Ally");
            int controlsTypeId = types.Register("Controls");
            int grantedFlagId = flags.Register("Granted");
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, allyTypeId);
            for (int i = 0; i < 4; i++)
            {
                runtime.EnsureLink(source, target, controlsTypeId);
                runtime.SetFlag(source, target, controlsTypeId, grantedFlagId, enabled: true);
                runtime.RemoveLink(source, target, controlsTypeId);
            }

            RelationshipChurnMeasurement measurement = MeasureTypedEdgeChurn(
                runtime,
                source,
                target,
                controlsTypeId,
                grantedFlagId,
                iterations: 1_024);
            RelationshipChurnMeasurement second = MeasureTypedEdgeChurn(
                runtime,
                source,
                target,
                controlsTypeId,
                grantedFlagId,
                iterations: 1_024);
            if (second.AllocatedBytes <= measurement.AllocatedBytes)
            {
                measurement = second;
            }

            Console.WriteLine(
                $"bench.relationship_pair_typed_churn iterations={measurement.Iterations} elapsed_ms={measurement.ElapsedMs:F2} alloc_bytes={measurement.AllocatedBytes}");

            Assert.Multiple(() =>
            {
                Assert.That(measurement.AllocatedBytes, Is.EqualTo(0),
                    "Warmed typed edge churn on an existing pair must stay allocation-free.");
                Assert.That(runtime.HasLink(source, target, allyTypeId), Is.True, "The stable pair edge must remain while the transient type churns.");
                Assert.That(runtime.HasLink(source, target, controlsTypeId), Is.False);
            });
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

        private static RelationshipChurnMeasurement MeasureTypedEdgeChurn(
            RelationshipRuntime runtime,
            Entity source,
            Entity target,
            int typeId,
            int flagId,
            int iterations)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                runtime.EnsureLink(source, target, typeId);
                runtime.SetFlag(source, target, typeId, flagId, enabled: true);
                if (!runtime.HasLink(source, target, typeId) ||
                    !runtime.HasFlag(source, target, typeId, flagId))
                {
                    throw new InvalidOperationException("Measured relationship churn lost the transient typed edge.");
                }

                runtime.RemoveLink(source, target, typeId);
            }

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return new RelationshipChurnMeasurement(
                iterations,
                Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds,
                allocated);
        }

        private readonly record struct RelationshipChurnMeasurement(
            int Iterations,
            double ElapsedMs,
            long AllocatedBytes);
    }
}
