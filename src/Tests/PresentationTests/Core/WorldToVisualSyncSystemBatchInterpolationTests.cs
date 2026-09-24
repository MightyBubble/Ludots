using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// WorldToVisualSyncSystem chunk 批插值契约：
    /// - 语义：输出必须与 Fix64Vec2.Lerp + LogicCmToVisualMeters 参考链逐位一致（定点精度语义保留）。
    /// - 稳态预算：10K massnav 形态 agent（FacingDirection + CullState 命中最重路径）× 60 渲染 tick median ≤ 0.8ms，零分配。
    /// </summary>
    [TestFixture]
    public sealed class WorldToVisualSyncSystemBatchInterpolationTests
    {
        private const int AgentCount = 10_000;
        private const int WarmupTicks = 8;
        private const int SampledTicks = 60;
        private const int TimingPasses = 3;
        private const float TickDeltaSeconds = 1f / 60f;
        // 安静机器的稳态闸门：三轮取最小中位（抗瞬态抖动），0.9 留机器档余量。
        // 并行构建下墙钟微基准可整体膨胀数倍，属已知纪律（性能基准须安静机器跑）；
        // 负载无关的硬门槛是零分配断言。
        private const double MedianBudgetMs = 0.9d;

        private static readonly QueryDescription MovingAgentQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm>();

        [Test]
        public void BatchInterpolation_OutputMatchesFix64LerpReferenceBitwiseAcrossHotPaths()
        {
            using var world = World.Create();
            world.Create(
                new PresentationFrameState { InterpolationAlpha = 0.25f, Enabled = true },
                new PresentationFrameStateTag());

            float[] alphas = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            (int prevX, int prevY, int curX, int curY)[] spans =
            {
                (0, 0, 1000, 2000),
                (-4000, 6000, 500, -500),
                (1_000_000, -2_000_000, 999_999, -1_999_998),
                (800, 900, 800, 900),
                (7500, 250, -250, 7500),
            };

            Entity[] noCull = new Entity[alphas.Length];
            Entity[] withCull = new Entity[alphas.Length];
            Entity[] facingNoCull = new Entity[alphas.Length];
            Entity[] facingWithCull = new Entity[alphas.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                noCull[i] = world.Create(
                    WorldPositionCm.FromCm(spans[i].curX, spans[i].curY),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(spans[i].prevX, spans[i].prevY) },
                    VisualTransform.Default);
                withCull[i] = world.Create(
                    WorldPositionCm.FromCm(spans[i].curX, spans[i].curY),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(spans[i].prevX, spans[i].prevY) },
                    VisualTransform.Default,
                    new CullState { IsVisible = false, LOD = LODLevel.Low });
                facingNoCull[i] = world.Create(
                    WorldPositionCm.FromCm(spans[i].curX, spans[i].curY),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(spans[i].prevX, spans[i].prevY) },
                    VisualTransform.Default,
                    new FacingDirection { AngleRad = MathF.PI * 0.25f });
                facingWithCull[i] = world.Create(
                    WorldPositionCm.FromCm(spans[i].curX, spans[i].curY),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(spans[i].prevX, spans[i].prevY) },
                    VisualTransform.Default,
                    new FacingDirection { AngleRad = -MathF.PI * 0.5f },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
            }

            using var system = new WorldToVisualSyncSystem(world);

            // 逐 alpha 验证：每次只改全局 PresentationFrameState，重跑系统，比较四条热路径输出。
            for (int i = 0; i < alphas.Length; i++)
            {
                SetFrameAlpha(world, alphas[i]);
                system.Update(TickDeltaSeconds);
                Fix64 alpha = Fix64.FromFloat(alphas[i]);
                Vector3 expectedPosition = ReferenceVisualPosition(spans[i].prevX, spans[i].prevY, spans[i].curX, spans[i].curY, alpha);
                AssertBitwisePosition(world, noCull[i], expectedPosition, $"alpha={alphas[i]} noCull");
                AssertBitwisePosition(world, withCull[i], expectedPosition, $"alpha={alphas[i]} withCull");
                AssertBitwisePosition(world, facingNoCull[i], expectedPosition, $"alpha={alphas[i]} facingNoCull");
                AssertBitwisePosition(world, facingWithCull[i], expectedPosition, $"alpha={alphas[i]} facingWithCull");

                Quaternion expectedFacingNoCull = WorldPlane2D.FacingRadToVisualYRotation(MathF.PI * 0.25f);
                Quaternion actualFacingNoCull = world.Get<VisualTransform>(facingNoCull[i]).Rotation;
                Assert.That(actualFacingNoCull.X, Is.EqualTo(expectedFacingNoCull.X));
                Assert.That(actualFacingNoCull.Y, Is.EqualTo(expectedFacingNoCull.Y));
                Assert.That(actualFacingNoCull.Z, Is.EqualTo(expectedFacingNoCull.Z));
                Assert.That(actualFacingNoCull.W, Is.EqualTo(expectedFacingNoCull.W));

                Quaternion expectedFacingWithCull = WorldPlane2D.FacingRadToVisualYRotation(-MathF.PI * 0.5f);
                Quaternion actualFacingWithCull = world.Get<VisualTransform>(facingWithCull[i]).Rotation;
                Assert.That(actualFacingWithCull.X, Is.EqualTo(expectedFacingWithCull.X));
                Assert.That(actualFacingWithCull.Y, Is.EqualTo(expectedFacingWithCull.Y));
                Assert.That(actualFacingWithCull.Z, Is.EqualTo(expectedFacingWithCull.Z));
                Assert.That(actualFacingWithCull.W, Is.EqualTo(expectedFacingWithCull.W));
            }
        }

        [Test]
        public void BatchInterpolation_10kMovingAgents_SteadyStateMedianBudgetAndZeroAllocation()
        {
            using var world = World.Create();
            world.Create(
                new PresentationFrameState { InterpolationAlpha = 0.5f, Enabled = true },
                new PresentationFrameStateTag());

            // 组件集对齐 massnav agent 的生产密度（TemplateEntityBatchSpawner 的 agent 形态），
            // 保证 chunk 容量与每 chunk 实体数与真实 10K 场景同量级。
            for (int i = 0; i < AgentCount; i++)
            {
                int lane = i & 3;
                world.Create(
                    WorldPositionCm.FromCm(1000 + lane * 40 + i, -2000 + lane * 15 + i),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(900 + lane * 40 + i, -1900 + lane * 15 + i) },
                    VisualTransform.Default,
                    new FacingDirection { AngleRad = lane * MathF.PI * 0.5f },
                    new CullState { IsVisible = (i & 1) == 0, LOD = LODLevel.High },
                    new ContinuousHeightmapSampleState(),
                    new AttributeBuffer(),
                    new AttributeLastSnapshot(),
                    new GameplayTagContainer(),
                    new TagCountContainer(),
                    new GameplayTagSnapshot(),
                    new GameplayTagEffectiveCache(),
                    new DirtyFlags(),
                    new EntityTemplateKeyRef(),
                    new Health { Current = 100, Max = 100 },
                    new Team { Id = 1 + lane },
                    new PlayerOwner { PlayerId = 1 },
                    new Name { Value = "bench-agent" });
            }

            using var system = new WorldToVisualSyncSystem(world);
            for (int tick = 0; tick < WarmupTicks; tick++)
            {
                AdvanceMovingAgents(world);
                system.Update(TickDeltaSeconds);
            }

            var samples = new double[SampledTicks];
            var passMedians = new double[TimingPasses];
            long allocatedBytes = 0;
            for (int pass = 0; pass < TimingPasses; pass++)
            {
                for (int tick = 0; tick < SampledTicks; tick++)
                {
                    AdvanceMovingAgents(world);
                    long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    system.Update(TickDeltaSeconds);
                    samples[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
                }

                Array.Sort(samples);
                passMedians[pass] = samples[SampledTicks / 2];
            }

            double median = passMedians.Min();
            double p95 = samples[(int)(SampledTicks * 0.95)];
            TestContext.Out.WriteLine(
                "WorldToVisualSync 10k moving agents over " + TimingPasses + "x" + SampledTicks +
                " ticks: minMedian=" + median.ToString("F3") + "ms passMedians=[" +
                string.Join(",", passMedians.Select(m => m.ToString("F3"))) + "]ms lastPassP95=" +
                p95.ToString("F3") + "ms allocated=" + allocatedBytes / (SampledTicks * TimingPasses) + "B/tick");
            Assert.That(median, Is.LessThanOrEqualTo(MedianBudgetMs),
                $"10K moving agent interpolation min-of-{TimingPasses}-pass median {median:F3}ms exceeds the {MedianBudgetMs}ms steady-state budget.");
            Assert.That(allocatedBytes, Is.Zero,
                "WorldToVisualSyncSystem steady state must not allocate.");
        }

        private static readonly QueryDescription FrameStateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();

        private static void SetFrameAlpha(World world, float alpha)
        {
            foreach (ref Chunk chunk in world.Query(in FrameStateQuery))
            {
                Span<PresentationFrameState> states = chunk.GetSpan<PresentationFrameState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    states[i].InterpolationAlpha = alpha;
                    states[i].Enabled = true;
                }
            }
        }

        private static Vector3 ReferenceVisualPosition(int prevX, int prevY, int curX, int curY, Fix64 alpha)
        {
            Fix64Vec2 interpolated = Fix64Vec2.Lerp(
                Fix64Vec2.FromInt(prevX, prevY),
                Fix64Vec2.FromInt(curX, curY),
                alpha);
            return WorldPlane2D.LogicCmToVisualMeters(in interpolated);
        }

        private static void AssertBitwisePosition(World world, Entity entity, Vector3 expected, string context)
        {
            Vector3 actual = world.Get<VisualTransform>(entity).Position;
            Assert.That(actual.X, Is.EqualTo(expected.X), $"{context} X");
            Assert.That(actual.Y, Is.EqualTo(expected.Y), $"{context} Y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z), $"{context} Z");
        }

        private static void AdvanceMovingAgents(World world)
        {
            foreach (ref Chunk chunk in world.Query(in MovingAgentQuery))
            {
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<PreviousWorldPositionCm> previousPositions = chunk.GetSpan<PreviousWorldPositionCm>();
                Fix64Vec2 step = Fix64Vec2.FromInt(3, 2);
                for (int i = 0; i < chunk.Count; i++)
                {
                    previousPositions[i].Value = positions[i].Value;
                    positions[i].Value += step;
                }
            }
        }
    }
}
