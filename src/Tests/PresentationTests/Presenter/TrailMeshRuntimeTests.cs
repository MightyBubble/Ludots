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

            runtime.Sample(entity, stableId: 42, in config, Vector3.Zero, Vector3.UnitZ, now: 1.0f);
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

            runtime.Sample(entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(entity, 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.05f);
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

            runtime.Sample(entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(entity, 7, in config, Vector3.UnitX, Vector3.UnitX + Vector3.UnitZ, now: 0.25f);
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

            runtime.Sample(entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
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

            runtime.Sample(entity, 7, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
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
        public void Sample_WithoutPositiveStableId_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 4);
            var runtime = new TrailMeshRuntime(buffer);
            Entity entity = world.Create();
            TrailMeshConfig config = DefaultConfig;

            Assert.Throws<InvalidOperationException>(
                () => runtime.Sample(entity, 0, in config, Vector3.Zero, Vector3.UnitZ, now: 0f));
        }

        [Test]
        public void Advance_WhenBufferOverflows_Throws()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: 1);
            var runtime = new TrailMeshRuntime(buffer);
            Entity first = world.Create();
            Entity second = world.Create();
            TrailMeshConfig config = DefaultConfig;

            runtime.Sample(first, 1, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);
            runtime.Sample(second, 2, in config, Vector3.Zero, Vector3.UnitZ, now: 0f);

            Assert.Throws<InvalidOperationException>(() => runtime.Advance(world, now: 0f));
        }
    }
}
