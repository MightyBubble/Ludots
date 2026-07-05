using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Relationships;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RelationshipReverseIndexTests
    {
        [Test]
        public void CollectIncoming_MatchesNaiveReferenceAfterRandomizedMutations()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var runtime = CreateRuntime(world, types, metrics, out _);
            int[] typeIds =
            {
                types.Register("SocialBond"),
                types.Register("Hostility"),
                types.Register("Owns"),
            };
            int loyaltyId = metrics.Register("Loyalty", minValue: -100, maxValue: 100, defaultValue: 0);

            var entities = new Entity[12];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = world.Create();
            }

            var reference = new HashSet<(Entity Source, Entity Target, int TypeId)>();
            var random = new Random(20260705);
            for (int step = 0; step < 2000; step++)
            {
                Entity source = entities[random.Next(entities.Length)];
                Entity target = entities[random.Next(entities.Length)];
                int typeId = typeIds[random.Next(typeIds.Length)];
                switch (random.Next(3))
                {
                    case 0:
                        runtime.EnsureLink(source, target, typeId);
                        reference.Add((source, target, typeId));
                        break;
                    case 1:
                        runtime.RemoveLink(source, target, typeId);
                        reference.Remove((source, target, typeId));
                        break;
                    default:
                        runtime.SetMetric(source, target, typeId, loyaltyId, random.Next(-100, 101));
                        reference.Add((source, target, typeId));
                        break;
                }
            }

            Span<Entity> buffer = stackalloc Entity[entities.Length + 4];
            foreach (Entity target in entities)
            {
                foreach (int typeId in typeIds)
                {
                    Entity[] expected = NaiveIncoming(reference, target, typeId);
                    int count = runtime.CollectIncoming(target, typeId, buffer);
                    Assert.That(buffer[..count].ToArray(), Is.EquivalentTo(expected), $"typeId {typeId}");
                }

                Entity[] expectedAny = NaiveIncoming(reference, target, RelationshipTypeRegistry.AnyTypeId);
                int anyCount = runtime.CollectIncoming(target, RelationshipTypeRegistry.AnyTypeId, buffer);
                Assert.That(buffer[..anyCount].ToArray(), Is.EquivalentTo(expectedAny), "AnyTypeId");
            }
        }

        [Test]
        public void CollectIncoming_TruncatesToBufferLengthWithoutDuplicates()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var runtime = CreateRuntime(world, types, new RelationshipMetricRegistry(), out _);
            int bondTypeId = types.Register("SocialBond");
            int hostilityTypeId = types.Register("Hostility");

            Entity target = world.Create();
            var sources = new Entity[6];
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = world.Create();
                runtime.EnsureLink(sources[i], target, bondTypeId);
                runtime.EnsureLink(sources[i], target, hostilityTypeId);
            }

            Span<Entity> small = stackalloc Entity[3];
            int truncated = runtime.CollectIncoming(target, bondTypeId, small);
            Assert.That(truncated, Is.EqualTo(3));
            Assert.That(small.ToArray().Distinct().Count(), Is.EqualTo(3));
            Assert.That(small.ToArray(), Is.SubsetOf(sources));

            int truncatedAny = runtime.CollectIncoming(target, RelationshipTypeRegistry.AnyTypeId, small);
            Assert.That(truncatedAny, Is.EqualTo(3));
            Assert.That(small.ToArray().Distinct().Count(), Is.EqualTo(3));
            Assert.That(small.ToArray(), Is.SubsetOf(sources));
        }

        [Test]
        public void CollectIncoming_MultiTypeEdgesOnSamePairStayPerTypeCorrect()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var runtime = CreateRuntime(world, types, new RelationshipMetricRegistry(), out _);
            int bondTypeId = types.Register("SocialBond");
            int hostilityTypeId = types.Register("Hostility");

            Entity source = world.Create();
            Entity target = world.Create();
            runtime.EnsureLink(source, target, bondTypeId);
            runtime.EnsureLink(source, target, hostilityTypeId);

            Span<Entity> buffer = stackalloc Entity[4];
            Assert.That(runtime.CollectIncoming(target, bondTypeId, buffer), Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(source));
            Assert.That(runtime.CollectIncoming(target, hostilityTypeId, buffer), Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(source));
            Assert.That(runtime.CollectIncoming(target, RelationshipTypeRegistry.AnyTypeId, buffer), Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(source));

            runtime.RemoveLink(source, target, bondTypeId);
            Assert.That(runtime.CollectIncoming(target, bondTypeId, buffer), Is.EqualTo(0));
            Assert.That(runtime.CollectIncoming(target, hostilityTypeId, buffer), Is.EqualTo(1));
        }

        [Test]
        public void CollectIncoming_SkipsDestroyedSourcesAndSurvivesDestroyedTarget()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var runtime = CreateRuntime(world, types, new RelationshipMetricRegistry(), out RelationshipReverseIndex index);
            int bondTypeId = types.Register("SocialBond");

            Entity target = world.Create();
            Entity survivor = world.Create();
            Entity doomed = world.Create();
            runtime.EnsureLink(survivor, target, bondTypeId);
            runtime.EnsureLink(doomed, target, bondTypeId);

            world.Destroy(doomed);

            Span<Entity> buffer = stackalloc Entity[4];
            int count = runtime.CollectIncoming(target, bondTypeId, buffer);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(survivor));

            world.Destroy(target);
            Assert.That(runtime.CollectIncoming(target, bondTypeId, buffer), Is.EqualTo(0));
            Assert.That(index.Compact(), Is.GreaterThanOrEqualTo(1));
            Assert.That(index.CopyIncoming(target, bondTypeId, buffer), Is.EqualTo(0));
        }

        [Test]
        public void Revision_IncreasesMonotonicallyOnEveryMutation()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var runtime = CreateRuntime(world, types, new RelationshipMetricRegistry(), out RelationshipReverseIndex index);
            int bondTypeId = types.Register("SocialBond");

            Entity source = world.Create();
            Entity target = world.Create();
            uint initial = index.Revision;

            runtime.EnsureLink(source, target, bondTypeId);
            uint afterAdd = index.Revision;
            Assert.That(afterAdd, Is.GreaterThan(initial));

            runtime.EnsureLink(source, target, bondTypeId);
            Assert.That(index.Revision, Is.EqualTo(afterAdd), "EnsureLink early-return must not mutate the index.");

            runtime.RemoveLink(source, target, bondTypeId);
            uint afterRemove = index.Revision;
            Assert.That(afterRemove, Is.GreaterThan(afterAdd));

            runtime.RemoveLink(source, target, bondTypeId);
            Assert.That(index.Revision, Is.EqualTo(afterRemove), "Removing a missing edge must not mutate the index.");
        }

        [Test]
        public void CopyIncoming_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var runtime = CreateRuntime(world, types, new RelationshipMetricRegistry(), out RelationshipReverseIndex index);
            int bondTypeId = types.Register("SocialBond");
            int hostilityTypeId = types.Register("Hostility");

            Entity target = world.Create();
            for (int i = 0; i < 16; i++)
            {
                Entity source = world.Create();
                runtime.EnsureLink(source, target, bondTypeId);
                runtime.EnsureLink(source, target, hostilityTypeId);
            }

            var buffer = new Entity[32];
            long allocated = MeasureCopyIncomingAllocations(index, target, bondTypeId, buffer);
            allocated = Math.Min(allocated, MeasureCopyIncomingAllocations(index, target, bondTypeId, buffer));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ScopeResolver_RelationshipIncomingMembership_ResolvesSourcesThroughIndex()
        {
            using var world = World.Create();
            var types = new RelationshipTypeRegistry();
            var runtime = CreateRuntime(world, types, new RelationshipMetricRegistry(), out _);
            int ownsTypeId = types.Register("Owns");
            var scopeKeys = new ScopeKeyRegistry();
            int ownersScopeId = scopeKeys.RegisterRelationshipIncomingMembers("owners", ownsTypeId);

            Entity host = world.Create(new ScopeMembershipRevision());
            Entity subject = world.Create();
            var refs = new ScopeRefBuffer();
            Assert.That(refs.TryAdd(ownersScopeId, host), Is.True);
            world.Add(subject, refs);

            Entity ownerA = world.Create();
            Entity ownerB = world.Create();
            Entity unrelated = world.Create();
            runtime.EnsureLink(ownerA, host, ownsTypeId);
            runtime.EnsureLink(ownerB, host, ownsTypeId);
            runtime.EnsureLink(host, unrelated, ownsTypeId);

            var resolver = new ScopeResolver(world, scopeKeys, relationships: runtime);
            var roles = new RoleResolverContext(subject: subject);
            Span<Entity> buffer = stackalloc Entity[4];
            int count = resolver.ResolveMembers(ScopeKey.Named(ownersScopeId), in roles, buffer);
            Assert.That(buffer[..count].ToArray(), Is.EquivalentTo(new[] { ownerA, ownerB }));
        }

        private static long MeasureCopyIncomingAllocations(
            RelationshipReverseIndex index,
            Entity target,
            int typeId,
            Entity[] buffer)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                index.CopyIncoming(target, typeId, buffer);
                index.CopyIncoming(target, RelationshipTypeRegistry.AnyTypeId, buffer);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static RelationshipRuntime CreateRuntime(
            World world,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            out RelationshipReverseIndex index)
        {
            index = new RelationshipReverseIndex(world);
            return new RelationshipRuntime(
                world,
                types,
                metrics,
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                index);
        }

        private static Entity[] NaiveIncoming(
            HashSet<(Entity Source, Entity Target, int TypeId)> edges,
            Entity target,
            int typeId)
        {
            var sources = new HashSet<Entity>();
            foreach ((Entity source, Entity edgeTarget, int edgeTypeId) in edges)
            {
                if (edgeTarget == target && (typeId == RelationshipTypeRegistry.AnyTypeId || edgeTypeId == typeId))
                {
                    sources.Add(source);
                }
            }

            return sources.ToArray();
        }
    }
}
