using Schedulers;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public class Navigation2DBenchmarkTests
    {
        private static readonly QueryDescription _benchmarkPoseQuery = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D>();

        private const int AgentCount = 10_000;
        private const int GridWidth = 100;
        private const int GridCellSizeCm = 100;
        private const float DeltaTime = 1f / 60f;
        private const float GoalDistanceCm = 6000f;
        private const float StaticCruiseSpeedCmPerSec = 240f;
        private const float InCellAmplitudeCm = 24f;
        private const float CrossCellAmplitudeCm = 135f;
        private const int MotionPeriodTicks = 24;

        [TestCase(Navigation2DAvoidanceMode.Orca, "ORCA", TestName = "Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_ORCA")]
        [TestCase(Navigation2DAvoidanceMode.Sonar, "Sonar", TestName = "Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_Sonar")]
        [TestCase(Navigation2DAvoidanceMode.Hybrid, "Hybrid", TestName = "Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_Hybrid")]
        public void Benchmark_Navigation2DSteering_StaticCrowd_10kAgents(Navigation2DAvoidanceMode mode, string label)
        {
            RunBenchmark(mode, label, NavBenchmarkScenario.StaticCrowd);
        }

        [TestCase(Navigation2DAvoidanceMode.Orca, "ORCA", TestName = "Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents_ORCA")]
        [TestCase(Navigation2DAvoidanceMode.Sonar, "Sonar", TestName = "Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents_Sonar")]
        [TestCase(Navigation2DAvoidanceMode.Hybrid, "Hybrid", TestName = "Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents_Hybrid")]
        public void Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents(Navigation2DAvoidanceMode mode, string label)
        {
            RunBenchmark(mode, label, NavBenchmarkScenario.OscillatingInCell);
        }

        [TestCase(Navigation2DAvoidanceMode.Orca, "ORCA", TestName = "Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_ORCA")]
        [TestCase(Navigation2DAvoidanceMode.Sonar, "Sonar", TestName = "Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Sonar")]
        [TestCase(Navigation2DAvoidanceMode.Hybrid, "Hybrid", TestName = "Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Hybrid")]
        public void Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents(Navigation2DAvoidanceMode mode, string label)
        {
            RunBenchmark(mode, label, NavBenchmarkScenario.QuarterCrossCellMigration);
        }

        private static void RunBenchmark(Navigation2DAvoidanceMode mode, string label, NavBenchmarkScenario scenario)
        {
            EnsureSharedScheduler();
            ScenarioRunConfig settings = GetScenarioRunConfig(scenario);
            PrimeBenchmarkCodePaths(mode, scenario, settings);

            var sampleAvgMs = new double[settings.SampleCount];
            var sampleAllocBytes = new long[settings.SampleCount];
            var sampleCellMapMs = new double[settings.SampleCount];
            var sampleDirtyAgentsPerTick = new double[settings.SampleCount];
            var sampleCellMigrationsPerTick = new double[settings.SampleCount];

            for (int sample = 0; sample < settings.SampleCount; sample++)
            {
                using var harness = CreateHarness(mode, scenario);
                WarmupScenario(harness, scenario, settings.WarmupIterations);
                harness.Runtime.CellMap.ResetInstrumentation();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.GetAllocatedBytesForCurrentThread();

                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
                long totalSteeringTicks = 0;

                for (int iteration = 0; iteration < settings.MeasuredIterations; iteration++)
                {
                    harness.ApplyScenarioStep(scenario, settings.WarmupIterations + iteration);
                    long t0 = Stopwatch.GetTimestamp();
                    harness.System.Update(DeltaTime);
                    totalSteeringTicks += Stopwatch.GetTimestamp() - t0;
                }

                long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
                sampleAvgMs[sample] = totalSteeringTicks * 1000.0 / Stopwatch.Frequency / settings.MeasuredIterations;
                sampleAllocBytes[sample] = afterAlloc - beforeAlloc;

                if (harness.Runtime.CellMap.InstrumentedUpdateCalls > 0)
                {
                    sampleCellMapMs[sample] = harness.Runtime.CellMap.InstrumentedUpdateTicks * 1000.0 / Stopwatch.Frequency / harness.Runtime.CellMap.InstrumentedUpdateCalls;
                    sampleDirtyAgentsPerTick[sample] = (double)harness.Runtime.CellMap.InstrumentedDirtyAgents / harness.Runtime.CellMap.InstrumentedUpdateCalls;
                    sampleCellMigrationsPerTick[sample] = (double)harness.Runtime.CellMap.InstrumentedCellMigrations / harness.Runtime.CellMap.InstrumentedUpdateCalls;
                }
            }

            PrintResult(mode, label, scenario, settings, sampleAvgMs, sampleAllocBytes, sampleCellMapMs, sampleDirtyAgentsPerTick, sampleCellMigrationsPerTick);
        }

        private static void PrimeBenchmarkCodePaths(Navigation2DAvoidanceMode mode, NavBenchmarkScenario scenario, ScenarioRunConfig settings)
        {
            using var harness = CreateHarness(mode, scenario);
            int primeIterations = Math.Max(128, settings.WarmupIterations + settings.MeasuredIterations * 2);
            for (int tick = 0; tick < primeIterations; tick++)
            {
                harness.ApplyScenarioStep(scenario, tick);
                harness.System.Update(DeltaTime);
            }
        }

        private static void EnsureSharedScheduler()
        {
            if (World.SharedJobScheduler != null)
            {
                return;
            }

            World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
            {
                ThreadPrefixName = "NavBenchmarkWorker",
                ThreadCount = 0,
                MaxExpectedConcurrentJobs = 64,
                StrictAllocationMode = false
            });
        }

        private static ScenarioRunConfig GetScenarioRunConfig(NavBenchmarkScenario scenario)
        {
            return scenario switch
            {
                NavBenchmarkScenario.QuarterCrossCellMigration => new ScenarioRunConfig(4, 12, 1, true),
                _ => new ScenarioRunConfig(10, 30, 2, false),
            };
        }

        private static BenchmarkHarness CreateHarness(Navigation2DAvoidanceMode mode, NavBenchmarkScenario scenario)
        {
            var world = World.Create();
            var runtime = new Navigation2DRuntime(CreateConfig(mode, maxAgents: AgentCount + 1024), gridCellSizeCm: GridCellSizeCm, loadedChunks: null)
            {
                FlowIterationsPerTick = 0,
                FlowEnabled = false
            };
            var system = new Navigation2DSteeringSystem2D(world, runtime);
            var anchors = new Vector2[AgentCount];
            var travelDirections = new Vector2[AgentCount];
            var phaseOffsets = new float[AgentCount];

            var kinematics = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(800),
                MaxAccelCmPerSec2 = Fix64.FromInt(8000),
                RadiusCm = Fix64.FromInt(30),
                NeighborDistCm = Fix64.FromInt(300),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 16
            };

            for (int i = 0; i < AgentCount; i++)
            {
                int row = i / GridWidth;
                int col = i % GridWidth;

                Vector2 anchor = new Vector2(col * GridCellSizeCm + 50f, row * GridCellSizeCm + 50f);
                Vector2 travelDirection = GetTravelDirection(scenario, row, col);
                float phaseOffset = GetPhaseOffset(row, col);
                GetScenarioPose(scenario, row, anchor, travelDirection, phaseOffset, tick: 0, out Vector2 position, out Vector2 velocity);
                if (scenario == NavBenchmarkScenario.StaticCrowd)
                {
                    position = anchor;
                    velocity = travelDirection * StaticCruiseSpeedCmPerSec;
                }

                Vector2 goal = anchor + travelDirection * GoalDistanceCm;
                world.Create(
                    new NavAgent2D(),
                    new NavGoal2D
                    {
                        Kind = NavGoalKind2D.Point,
                        TargetCm = Fix64Vec2.FromVector2(goal),
                        RadiusCm = Fix64.Zero
                    },
                    kinematics,
                    new Position2D { Value = Fix64Vec2.FromVector2(position) },
                    new Velocity2D { Linear = Fix64Vec2.FromVector2(velocity), Angular = Fix64.Zero },
                    Mass2D.FromFloat(1f, 1f),
                    new ForceInput2D { Force = Fix64Vec2.Zero },
                    new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
                );

                anchors[i] = anchor;
                travelDirections[i] = travelDirection;
                phaseOffsets[i] = phaseOffset;
            }

            return new BenchmarkHarness(world, runtime, system, anchors, travelDirections, phaseOffsets);
        }

        private static void WarmupScenario(BenchmarkHarness harness, NavBenchmarkScenario scenario, int warmupIterations)
        {
            for (int tick = 0; tick < warmupIterations; tick++)
            {
                harness.ApplyScenarioStep(scenario, tick);
                harness.System.Update(DeltaTime);
            }
        }

        private static Vector2 GetTravelDirection(NavBenchmarkScenario scenario, int row, int col)
        {
            if (scenario == NavBenchmarkScenario.QuarterCrossCellMigration && (row & 1) != 0)
            {
                float verticalSign = (col & 1) == 0 ? 1f : -1f;
                return new Vector2(0f, verticalSign);
            }

            float horizontalSign = (row & 1) == 0 ? 1f : -1f;
            return new Vector2(horizontalSign, 0f);
        }

        private static float GetPhaseOffset(int row, int col)
        {
            return ((row + col) & 3) * (MathF.PI * 0.5f);
        }

        private static bool IsMigratingRow(int row)
        {
            return (row & 3) == 0;
        }

        private static void GetScenarioPose(
            NavBenchmarkScenario scenario,
            int row,
            in Vector2 anchor,
            in Vector2 travelDirection,
            float phaseOffset,
            int tick,
            out Vector2 position,
            out Vector2 velocity)
        {
            if (scenario == NavBenchmarkScenario.StaticCrowd || (scenario == NavBenchmarkScenario.QuarterCrossCellMigration && !IsMigratingRow(row)))
            {
                position = anchor;
                velocity = travelDirection * StaticCruiseSpeedCmPerSec;
                return;
            }

            float amplitude = scenario switch
            {
                NavBenchmarkScenario.OscillatingInCell => InCellAmplitudeCm,
                NavBenchmarkScenario.QuarterCrossCellMigration => CrossCellAmplitudeCm,
                _ => 0f
            };

            float angleStep = 2f * MathF.PI / MotionPeriodTicks;
            float angle = tick * angleStep + phaseOffset;
            float displacement = amplitude * MathF.Sin(angle);
            float speed = amplitude * angleStep / DeltaTime * MathF.Cos(angle);
            position = anchor + travelDirection * displacement;
            velocity = travelDirection * speed;
        }

        private static void PrintResult(
            Navigation2DAvoidanceMode mode,
            string label,
            NavBenchmarkScenario scenario,
            ScenarioRunConfig settings,
            double[] sampleAvgMs,
            long[] sampleAllocBytes,
            double[] sampleCellMapMs,
            double[] sampleDirtyAgentsPerTick,
            double[] sampleCellMigrationsPerTick)
        {
            var sortedMs = (double[])sampleAvgMs.Clone();
            Array.Sort(sortedMs);
            var sortedAllocs = (long[])sampleAllocBytes.Clone();
            Array.Sort(sortedAllocs);

            Console.WriteLine($"[Benchmark] Navigation2DSteeringSystem2D / {GetScenarioName(scenario)} / {label}");
            Console.WriteLine($"  Mode: {mode}");
            Console.WriteLine($"  Agents: {AgentCount}");
            Console.WriteLine($"  Warmup Iterations: {settings.WarmupIterations}");
            Console.WriteLine($"  Measured Iterations: {settings.MeasuredIterations}");
            Console.WriteLine($"  Samples: {settings.SampleCount}");
            Console.WriteLine($"  Median Avg Steering Tick: {MedianOfSorted(sortedMs):F4}ms");
            Console.WriteLine($"  Min/Max Avg Steering Tick: {sortedMs[0]:F4}ms / {sortedMs[^1]:F4}ms");
            Console.WriteLine($"  Sample Avg Steering Tick: {FormatSamples(sampleAvgMs)}");
            Console.WriteLine($"  Median AllocatedBytes(CurrentThread): {MedianOfSorted(sortedAllocs)}");

            if (settings.PrintCellMapStats)
            {
                Console.WriteLine($"  CellMap Avg UpdatePositions Tick: {MedianOfSorted((double[])sampleCellMapMs.Clone()):F4}ms");
                Console.WriteLine($"  CellMap Dirty Agents/Tick: {MedianOfSorted((double[])sampleDirtyAgentsPerTick.Clone()):F1}");
                Console.WriteLine($"  CellMap Cell Migrations/Tick: {MedianOfSorted((double[])sampleCellMigrationsPerTick.Clone()):F1}");
                Console.WriteLine($"  Migrating Rows: {GridWidth / 4}");
                Console.WriteLine($"  Expected Migrating Agents/Tick: {AgentCount / 4}");
            }
        }

        private static string GetScenarioName(NavBenchmarkScenario scenario)
        {
            return scenario switch
            {
                NavBenchmarkScenario.StaticCrowd => "StaticCrowd",
                NavBenchmarkScenario.OscillatingInCell => "OscillatingInCell",
                NavBenchmarkScenario.QuarterCrossCellMigration => "QuarterCrossCellMigration",
                _ => scenario.ToString()
            };
        }

        private static string FormatSamples(double[] samples)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < samples.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(samples[i].ToString("F4"));
                sb.Append("ms");
            }

            return sb.ToString();
        }

        private static double MedianOfSorted(double[] values)
        {
            Array.Sort(values);
            int mid = values.Length / 2;
            return (values.Length & 1) != 0
                ? values[mid]
                : (values[mid - 1] + values[mid]) * 0.5;
        }

        private static long MedianOfSorted(long[] sorted)
        {
            int mid = sorted.Length / 2;
            return (sorted.Length & 1) != 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        private static Navigation2DConfig CreateConfig(Navigation2DAvoidanceMode mode, int maxAgents)
        {
            return new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = maxAgents,
                FlowIterationsPerTick = 0,
                Spatial = new Navigation2DSpatialPartitionConfig
                {
                    UpdateMode = Navigation2DSpatialUpdateMode.Adaptive,
                    RebuildCellMigrationsThreshold = 128,
                    RebuildAccumulatedCellMigrationsThreshold = 1024,
                },
                Steering = new Navigation2DSteeringConfig
                {
                    Mode = mode,
                    QueryBudget = new Navigation2DQueryBudgetConfig
                    {
                        MaxNeighborsPerAgent = 8,
                        MaxCandidateChecksPerAgent = 24,
                    },
                    Orca = new Navigation2DOrcaConfig
                    {
                        Enabled = true,
                        FallbackToPreferredVelocity = true,
                    },
                    Sonar = new Navigation2DSonarConfig
                    {
                        Enabled = true,
                        MaxSteerAngleDeg = 280,
                        BackwardPenaltyAngleDeg = 230,
                        IgnoreBehindMovingAgents = true,
                        BlockedStop = false,
                        PredictionTimeScale = 0.9f,
                    },
                    Hybrid = new Navigation2DHybridAvoidanceConfig
                    {
                        Enabled = true,
                        DenseNeighborThreshold = 6,
                        MinSpeedForOrcaCmPerSec = 120,
                        MinOpposingNeighborsForOrca = 1,
                        OpposingVelocityDotThreshold = -0.25f,
                    },
                    SmartStop = new Navigation2DSmartStopConfig
                    {
                        Enabled = false,
                        QueryRadiusCm = 100,
                        MaxNeighbors = 6,
                        SelfGoalDistanceLimitCm = 160,
                        GoalToleranceCm = 80,
                        ArrivedSlackCm = 20,
                        StoppedSpeedThresholdCmPerSec = 5,
                    }
                }
            };
        }

        private enum NavBenchmarkScenario
        {
            StaticCrowd,
            OscillatingInCell,
            QuarterCrossCellMigration,
        }

        private readonly record struct ScenarioRunConfig(int WarmupIterations, int MeasuredIterations, int SampleCount, bool PrintCellMapStats);

        private sealed class BenchmarkHarness : IDisposable
        {
            public readonly World World;
            public readonly Navigation2DRuntime Runtime;
            public readonly Navigation2DSteeringSystem2D System;
            private readonly Vector2[] _anchors;
            private readonly Vector2[] _travelDirections;
            private readonly float[] _phaseOffsets;

            public BenchmarkHarness(
                World world,
                Navigation2DRuntime runtime,
                Navigation2DSteeringSystem2D system,
                Vector2[] anchors,
                Vector2[] travelDirections,
                float[] phaseOffsets)
            {
                World = world;
                Runtime = runtime;
                System = system;
                _anchors = anchors;
                _travelDirections = travelDirections;
                _phaseOffsets = phaseOffsets;
            }

            public void ApplyScenarioStep(NavBenchmarkScenario scenario, int tick)
            {
                if (scenario == NavBenchmarkScenario.StaticCrowd)
                {
                    return;
                }

                int agentIndex = 0;
                foreach (ref var chunk in World.Query(in _benchmarkPoseQuery))
                {
                    chunk.GetSpan<Position2D, Velocity2D>(out var positions, out var velocities);
                    foreach (var entityIndex in chunk)
                    {
                        int row = agentIndex / GridWidth;
                        if (scenario == NavBenchmarkScenario.QuarterCrossCellMigration && !IsMigratingRow(row))
                        {
                            agentIndex++;
                            continue;
                        }

                        GetScenarioPose(scenario, row, _anchors[agentIndex], _travelDirections[agentIndex], _phaseOffsets[agentIndex], tick, out Vector2 position, out Vector2 velocity);
                        positions[entityIndex] = new Position2D { Value = Fix64Vec2.FromVector2(position) };
                        velocities[entityIndex] = new Velocity2D { Linear = Fix64Vec2.FromVector2(velocity), Angular = Fix64.Zero };
                        agentIndex++;
                    }
                }
            }

            public void Dispose()
            {
                Runtime.Dispose();
                World.Dispose();
            }
        }
    }
}
