using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 §5.4 mind_control-style unit-scope grants: a Controls edge to a plain unit makes the unit's
    /// domain partially controlled, and <see cref="ControlPlaneView"/> projects only the controllable rows of
    /// that domain. Domain collections are written directly through the store to keep the scenario independent
    /// from the routed writer.
    /// </summary>
    [TestFixture]
    public sealed class ControlPlaneViewUnitGrantTests
    {
        [Test]
        public void UnitGrant_ProjectsOnlyGrantedRowsOfTheForeignDomain()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p3Rep = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity mOwn = world.Create();
            Entity mEnemy1 = world.Create();
            Entity mEnemy2 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, mOwn);
            harness.Ownership.EnsureOwnership(p3Rep, mEnemy1);
            harness.Ownership.EnsureOwnership(p3Rep, mEnemy2);
            harness.Relationships.EnsureLink(p1Rep, mEnemy1, harness.ControlsTypeId);

            harness.ReplaceDomainCollection(p1Rep, stackalloc Entity[] { mOwn });
            harness.ReplaceDomainCollection(p3Rep, stackalloc Entity[] { mEnemy1, mEnemy2 });

            Span<Entity> members = stackalloc Entity[8];
            Span<Entity> domains = stackalloc Entity[8];
            int count = harness.View.CopyMembersWithDomain(p1Rep, harness.CommandSourceKeyId, members, domains);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { mOwn, mEnemy1 }),
                "The partially controlled domain must contribute only the granted unit, never its siblings.");
            Assert.That(domains[..count].ToArray(), Is.EqualTo(new[] { p1Rep, p3Rep }),
                "Row provenance must point at the domain the row lives in.");
        }

        [Test]
        public void UnitGrantRevoke_ShrinksViewWhileForeignDomainKeepsItsRows()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p3Rep = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity mOwn = world.Create();
            Entity mEnemy1 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, mOwn);
            harness.Ownership.EnsureOwnership(p3Rep, mEnemy1);
            harness.Relationships.EnsureLink(p1Rep, mEnemy1, harness.ControlsTypeId);

            harness.ReplaceDomainCollection(p1Rep, stackalloc Entity[] { mOwn });
            harness.ReplaceDomainCollection(p3Rep, stackalloc Entity[] { mEnemy1 });

            Span<Entity> members = stackalloc Entity[8];
            int count = harness.View.CopyMembers(p1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { mOwn, mEnemy1 }));

            harness.Relationships.RemoveLink(p1Rep, mEnemy1, harness.ControlsTypeId);

            count = harness.View.CopyMembers(p1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { mOwn }),
                "Revoking the unit grant must shrink the view to the anchor's own domain.");

            Assert.That(harness.Store.TryGet(p3Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p3Handle), Is.True);
            Span<Entity> rows = stackalloc Entity[4];
            Assert.That(harness.Store.CopyEntities(p3Handle, 0, rows), Is.EqualTo(1),
                "The foreign domain keeps its rows; only the composite view contracts.");
            Assert.That(rows[0], Is.EqualTo(mEnemy1));
        }

        [Test]
        public void ComputeRevision_MovesOnUnitGrantAndRevoke()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p3Rep = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity mEnemy1 = world.Create();
            harness.Ownership.EnsureOwnership(p3Rep, mEnemy1);
            harness.ReplaceDomainCollection(p3Rep, stackalloc Entity[] { mEnemy1 });

            uint initial = harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId);
            Assert.That(harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId), Is.EqualTo(initial),
                "Stable state must yield a stable revision.");

            harness.Relationships.EnsureLink(p1Rep, mEnemy1, harness.ControlsTypeId);
            uint afterGrant = harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId);
            Assert.That(afterGrant, Is.Not.EqualTo(initial), "A unit grant is a topology change and must move the revision.");

            harness.Relationships.RemoveLink(p1Rep, mEnemy1, harness.ControlsTypeId);
            Assert.That(harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId), Is.Not.EqualTo(afterGrant),
                "Revoking the unit grant must move the revision again.");
        }

        [Test]
        public void UnitGrantViewRead_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p3Rep = world.Create(new PlayerIdentity { PlayerId = 3 });
            Span<Entity> p1Members = stackalloc Entity[4];
            Span<Entity> p3Members = stackalloc Entity[8];
            for (int i = 0; i < 4; i++)
            {
                Entity own = world.Create();
                harness.Ownership.EnsureOwnership(p1Rep, own);
                p1Members[i] = own;
            }

            for (int i = 0; i < 8; i++)
            {
                Entity enemy = world.Create();
                harness.Ownership.EnsureOwnership(p3Rep, enemy);
                p3Members[i] = enemy;
            }

            harness.Relationships.EnsureLink(p1Rep, p3Members[0], harness.ControlsTypeId);
            harness.ReplaceDomainCollection(p1Rep, p1Members);
            harness.ReplaceDomainCollection(p3Rep, p3Members);

            var members = new Entity[32];
            var domains = new Entity[32];
            harness.View.CopyMembersWithDomain(p1Rep, harness.CommandSourceKeyId, members, domains);
            harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId);

            long allocated = MeasureViewReadAllocations(harness, p1Rep, members, domains);
            allocated = Math.Min(allocated, MeasureViewReadAllocations(harness, p1Rep, members, domains));
            Assert.That(allocated, Is.EqualTo(0));
        }

        /// <summary>
        /// Budget guard for the documented partial-domain complexity contract: one O(rows) hash-probe pass.
        /// A 50k-row foreign collection with 64 direct unit grants must project exactly the granted rows in
        /// collection order and stay allocation-free in steady state.
        /// </summary>
        [Test]
        public void PartialDomainProjection_FiftyThousandRowForeignCollection_StaysWithinBudgetAndAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p3Rep = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity mOwn = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, mOwn);

            const int foreignRows = 50_000;
            const int grantCount = 64;
            const int iterations = 80;
            var foreign = new Entity[foreignRows];
            for (int i = 0; i < foreignRows; i++)
            {
                foreign[i] = world.Create();
                harness.Ownership.EnsureOwnership(p3Rep, foreign[i]);
            }

            var granted = new Entity[grantCount];
            for (int i = 0; i < grantCount; i++)
            {
                granted[i] = foreign[i * (foreignRows / grantCount) + 3];
                harness.Relationships.EnsureLink(p1Rep, granted[i], harness.ControlsTypeId);
            }

            harness.ReplaceDomainCollection(p1Rep, stackalloc Entity[] { mOwn });
            harness.ReplaceDomainCollection(p3Rep, foreign);

            var expected = new Entity[grantCount + 1];
            expected[0] = mOwn;
            Array.Copy(granted, 0, expected, 1, grantCount);

            var members = new Entity[grantCount + 4];
            int count = harness.View.CopyMembers(p1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count], Is.EqualTo(expected),
                "The partial domain must contribute exactly the granted rows, in collection order.");

            PartialProjectionBudgetMeasurement measurement = MeasureStablePartialProjectionBudget(
                harness,
                p1Rep,
                members,
                iterations,
                expectedRowsPerRead: grantCount + 1,
                foreignRows);

            Console.WriteLine(
                $"bench.control_plane_partial_view foreign_rows={foreignRows} direct_grants={grantCount} " +
                $"returned={measurement.RowsPerRead} iterations={iterations} elapsed_ms={measurement.ElapsedMs:F2} " +
                $"ns_per_foreign_row={measurement.NsPerForeignRow:F1} alloc_bytes={measurement.AllocatedBytes}");

            Assert.Multiple(() =>
            {
                Assert.That(measurement.Checksum, Is.EqualTo(iterations * (grantCount + 1)));
                Assert.That(measurement.AllocatedBytes, Is.EqualTo(0));
                Assert.That(measurement.NsPerForeignRow, Is.LessThan(250d),
                    "Partial-domain projection has a documented O(rows) pass; keep the per-row slope bounded.");
            });
        }

        private static PartialProjectionBudgetMeasurement MeasureStablePartialProjectionBudget(
            Harness harness,
            Entity anchorRep,
            Entity[] members,
            int iterations,
            int expectedRowsPerRead,
            int foreignRows)
        {
            harness.View.CopyMembers(anchorRep, harness.CommandSourceKeyId, members);
            harness.View.CopyMembers(anchorRep, harness.CommandSourceKeyId, members);
            PartialProjectionBudgetMeasurement first = MeasurePartialProjectionBudget(
                harness,
                anchorRep,
                members,
                iterations,
                expectedRowsPerRead,
                foreignRows);
            PartialProjectionBudgetMeasurement second = MeasurePartialProjectionBudget(
                harness,
                anchorRep,
                members,
                iterations,
                expectedRowsPerRead,
                foreignRows);
            return second.AllocatedBytes <= first.AllocatedBytes ? second : first;
        }

        private static PartialProjectionBudgetMeasurement MeasurePartialProjectionBudget(
            Harness harness,
            Entity anchorRep,
            Entity[] members,
            int iterations,
            int expectedRowsPerRead,
            int foreignRows)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            int checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                int count = harness.View.CopyMembers(anchorRep, harness.CommandSourceKeyId, members);
                if (count != expectedRowsPerRead)
                {
                    throw new InvalidOperationException($"Expected {expectedRowsPerRead} projected rows, got {count}.");
                }

                checksum += count;
            }

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double elapsedMs = Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds;
            double nsPerForeignRow = elapsedMs * 1_000_000d / (iterations * (double)foreignRows);
            return new PartialProjectionBudgetMeasurement(
                checksum,
                expectedRowsPerRead,
                elapsedMs,
                nsPerForeignRow,
                allocated);
        }

        private static long MeasureViewReadAllocations(Harness harness, Entity anchorRep, Entity[] members, Entity[] domains)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.View.CopyMembersWithDomain(anchorRep, harness.CommandSourceKeyId, members, domains);
                harness.View.ComputeRevision(anchorRep, harness.CommandSourceKeyId);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public EntityCollectionStore Store = null!;
            public ControlPlaneView View = null!;
            public int ControlsTypeId;
            public int CommandSourceKeyId;

            private EntityCollectionDescriptor _descriptor;

            public void ReplaceDomainCollection(Entity domainRep, ReadOnlySpan<Entity> entities)
            {
                Store.Replace(domainRep, in _descriptor, entities, domainRep);
            }

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
                var query = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
                var keyRegistry = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var store = new EntityCollectionStore(keyRegistry, initialCollectionCapacity: 16, initialRowCapacity: 128);
                return new Harness
                {
                    Relationships = relationships,
                    Ownership = ownership,
                    Store = store,
                    View = new ControlPlaneView(store, query),
                    ControlsTypeId = controlsTypeId,
                    CommandSourceKeyId = keyRegistry.Register(EntityCollectionKeys.CommandSource),
                    _descriptor = EntityCollectionDescriptor.Create(
                        EntityCollectionKeys.CommandSource,
                        EntityCollectionSourceKind.UiAcquisition,
                        EntityCollectionRoleKind.CommandSource),
                };
            }
        }

        private readonly record struct PartialProjectionBudgetMeasurement(
            int Checksum,
            int RowsPerRead,
            double ElapsedMs,
            double NsPerForeignRow,
            long AllocatedBytes);
    }
}
