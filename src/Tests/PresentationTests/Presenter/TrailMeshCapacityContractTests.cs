using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// TrailMesh fixed-capacity contract at true scale: the buffer is exercised at the
    /// capacity LudotsCoreMod authorizes in game.json (presentation.trailMeshCapacity),
    /// and the steady-state sample/advance producer path must not allocate per frame.
    /// </summary>
    [TestFixture]
    public sealed class TrailMeshCapacityContractTests
    {
        private const int TrueScaleCapacity = 64;

        [Test]
        public void TrailMesh_AtConfiguredCapacity_ZeroAllocationAfterWarmup()
        {
            using var world = World.Create();
            var buffer = new TrailMeshBuffer(capacity: TrueScaleCapacity);
            var runtime = new TrailMeshRuntime(buffer);
            TrailMeshConfig config = new()
            {
                BaseOffset = Vector3.Zero,
                TipOffset = Vector3.UnitZ,
                MaxSamples = 8,
                SampleIntervalSeconds = 0f,
                SampleLifetimeSeconds = 1f,
                HeadColor = Vector4.One,
                TailColor = new Vector4(1f, 1f, 1f, 0f),
            };

            var entities = new Entity[TrueScaleCapacity];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = world.Create();
            }

            // Warmup: fill the bounded fixed-capacity path (sampler pool, indices,
            // retained strips) to steady state and JIT the hot loop.
            for (int frame = 0; frame < 180; frame++)
            {
                Step(world, runtime, entities, config, frame);
            }

            Assert.That(buffer.Count, Is.EqualTo(TrueScaleCapacity), "fixed-capacity path must be exercised at full configured capacity");
            Assert.That(runtime.ActiveCount, Is.EqualTo(TrueScaleCapacity));

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 240; frame++)
            {
                Step(world, runtime, entities, config, frame);
            }

            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(
                allocatedAfter - allocatedBefore,
                Is.Zero,
                "steady-state TrailMesh sample/advance at configured capacity must not allocate per frame");
        }

        private static void Step(
            World world,
            TrailMeshRuntime runtime,
            Entity[] entities,
            TrailMeshConfig config,
            int frame)
        {
            float now = frame * (1f / 60f);
            for (int i = 0; i < entities.Length; i++)
            {
                float angle = now * 2f + i;
                Vector3 baseWorld = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
                runtime.Sample(entities[i], stableId: i + 1, in config, baseWorld, baseWorld + Vector3.UnitZ, now);
            }

            runtime.Advance(world, now);
        }
    }
}
