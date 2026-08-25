using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class TrailMeshRuntimeTests
    {
        private static TrailMeshConfig DefaultConfig => new()
        {
            BaseOffset = Vector3.Zero,
            TipOffset = Vector3.UnitZ,
            MaxSamples = 8,
            SampleIntervalSeconds = 0f,
            SampleLifetimeSeconds = 0.5f,
            HeadColor = Vector4.One,
            TailColor = new Vector4(1f, 1f, 1f, 0f),
        };

        [Test]
        public void SampleThenAdvance_EmitsSnapshotIntoBuffer()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, entity, stableId: 42, in config, Vector3.Zero, Vector3.UnitZ, now: 1.0f);
            runtime.Advance(world, now: 1.0f);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.GetStableId(0), Is.EqualTo(42));
            ReadOnlySpan<TrailMeshSample> samples = buffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(1));
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.Zero));
            Assert.That(samples[0].Tip, Is.EqualTo(Vector3.UnitZ));
            Assert.That(samples[0].Age01, Is.EqualTo(0f));
        }

        [Test]
        public void Sample_WithinInterval_PinsHeadInsteadOfAppending()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;
            config.SampleIntervalSeconds = 0.1f;

            runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(world, entity, 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.05f);
            runtime.Advance(world, now: 0.05f);

            ReadOnlySpan<TrailMeshSample> samples = buffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(1), "sub-interval resample must refresh the head in place");
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.UnitX));
        }

        [Test]
        public void Sample_PastInterval_AppendsAndOldestCarriesAge()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(world, entity, 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.25f);
            runtime.Advance(world, now: 0.25f);

            ReadOnlySpan<TrailMeshSample> samples = buffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(2));
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.UnitX), "index 0 is the newest head sample");
            Assert.That(samples[0].Age01, Is.EqualTo(0f));
            Assert.That(samples[1].Age01, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Advance_SamplesOlderThanLifetime_DropsTailThenRemovesEmptyTrail()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Advance(world, now: 0.2f);
            Assert.That(buffer.Count, Is.EqualTo(1));

            runtime.Advance(world, now: 0.6f);
            Assert.That(buffer.Count, Is.EqualTo(0), "fully faded trail must leave the buffer");
            Assert.That(runtime.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Advance_WhenEntityDies_RemovesTrailImmediately()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Advance(world, now: 0f);
            Assert.That(buffer.Count, Is.EqualTo(1));

            world.Destroy(entity);
            runtime.Advance(world, now: 0.05f);

            Assert.That(buffer.Count, Is.EqualTo(0));
            Assert.That(runtime.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Advance_WipesForeignEntriesNotRefreshedThisFrame()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            TrailMeshSample[] foreign = { new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f } };
            Assert.That(buffer.Upsert(999, foreign, Vector4.One, Vector4.Zero), Is.True);

            var runtime = new TrailMeshRuntime(buffer);
            runtime.Advance(world, now: 0f);

            Assert.That(buffer.Count, Is.EqualTo(0), "stale entries from a previous world/session must be reclaimed");
        }

        [Test]
        public void Sample_DeadOwnerReleasesClaim_NewPresenterReusesStableIdInSameFrame()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity first = world.Create();
            Entity second = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, first, stableId: 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            world.Destroy(first);

            // 旧属主同帧内死亡、Advance 尚未清扫：新 presenter 复用同一 stableId 是合法回收，
            // 不得误报重复声明。
            Assert.DoesNotThrow(
                () => runtime.Sample(world, second, stableId: 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.05f));

            runtime.Advance(world, now: 0.05f);

            Assert.That(runtime.ActiveCount, Is.EqualTo(1));
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.GetStableId(0), Is.EqualTo(7));
            ReadOnlySpan<TrailMeshSample> samples = buffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(1));
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.UnitX), "释放旧属主声明后，新属主的条带接管缓冲槽位");
        }

        [Test]
        public void Sample_RecycledEntityIdWithinSameFrame_ResamplesCleanly()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity first = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, first, stableId: 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            world.Destroy(first);

            // Arch 可能在同一帧复用刚销毁的 entity id：新实体带着同一 stableId 采样，
            // 必须从全新轨迹开始而不是撞上残留声明。
            Entity recycled = world.Create();
            Assert.DoesNotThrow(
                () => runtime.Sample(world, recycled, stableId: 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.05f));

            runtime.Advance(world, now: 0.05f);

            Assert.That(runtime.ActiveCount, Is.EqualTo(1));
            Assert.That(buffer.Count, Is.EqualTo(1));
            ReadOnlySpan<TrailMeshSample> samples = buffer.GetSamples(0);
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.UnitX));
        }

        [Test]
        public void Sample_TwoLivePresentersWithSameStableId_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity first = world.Create();
            Entity second = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, first, stableId: 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, second, stableId: 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0f),
                "two live samplers sharing a stableId must fail fast instead of silently overwriting one buffer slot");
        }

        [Test]
        public void Sample_EntityResamplesWithNewStableId_StartsFreshTrailAndReleasesOldClaim()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, entity, stableId: 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(world, entity, stableId: 8, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.1f);
            runtime.Advance(world, now: 0.1f);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.GetStableId(0), Is.EqualTo(8));
            ReadOnlySpan<TrailMeshSample> samples = buffer.GetSamples(0);
            Assert.That(samples.Length, Is.EqualTo(1));
            Assert.That(samples[0].Base, Is.EqualTo(Vector3.UnitX));
        }

        [Test]
        public void Sample_EntityRecyclingOntoAnotherLivePresentersStableId_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity first = world.Create();
            Entity second = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(world, first, stableId: 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(world, second, stableId: 8, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, first, stableId: 8, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.1f));
        }

        [Test]
        public void Sample_WhenSamplerCapacityMatchesBufferCapacity_ThrowsAtConfiguredBoundary()
        {
            using var world = World.Create();
            // capacity 8 sits below MaxSamplesPerTrail * 2 (64): the old floor would
            // silently admit 64 samplers; the contract is sampler capacity == buffer.Capacity.
            var buffer = new TrailMeshBuffer(capacity: 8);
            var runtime = new TrailMeshRuntime(buffer);
            TrailMeshConfig config = DefaultConfig;
            var entities = new Entity[9];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = world.Create();
            }

            for (int i = 0; i < 8; i++)
            {
                runtime.Sample(world, entities[i], stableId: i + 1, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            }

            Assert.That(runtime.ActiveCount, Is.EqualTo(8));
            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, entities[8], stableId: 9, in config, Vector3.Zero, Vector3.UnitZ, now: 0f));
        }

        [Test]
        public void Sample_ConfigWithOutOfRangeMaxSamples_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;
            config.MaxSamples = 1;

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f));
        }

        [Test]
        public void Sample_ConfigWithNegativeInterval_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;
            config.SampleIntervalSeconds = -0.1f;

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f));
        }

        [Test]
        public void Sample_ConfigWithNonPositiveLifetime_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;
            config.SampleLifetimeSeconds = 0f;

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f));
        }

        [Test]
        public void Sample_WithoutPositiveStableId_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(world, entity, 0, in config, Vector3.Zero, Vector3.UnitZ, now: 0f));
        }

        [Test]
        public void Advance_WhenForeignEntriesFillBuffer_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 2);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;
            runtime.Sample(world, entity, 1, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);

            // A writer outside this runtime fills every remaining slot after the last
            // drain; the emit pass must fail at the configured boundary, not admit
            // an extra strip silently.
            TrailMeshSample[] foreign = { new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f } };
            Assert.That(buffer.Upsert(90, foreign, Vector4.One, Vector4.Zero), Is.True);
            Assert.That(buffer.Upsert(91, foreign, Vector4.One, Vector4.Zero), Is.True);

            Assert.Throws<InvalidOperationException>(() => runtime.Advance(world, now: 0f));
        }
    }
}
