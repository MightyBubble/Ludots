using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
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
    public sealed class Navigation2DSteeringTemporalCoherenceTests
    {
        private const int AgentsPerTeam = 16;
        private const int SimulationTicks = 6;
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void TemporalCoherence_StaticOpposingCrowd_ProducesHitsAndAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-steering-temporal-coherence");
            Directory.CreateDirectory(artifactDir);

            var reference = RunScenario(cacheEnabled: false);
            var cached = RunScenario(cacheEnabled: true);

            Assert.That(cached.TotalCacheLookups, Is.GreaterThan(0));
            Assert.That(cached.TotalCacheHits, Is.GreaterThan(0));
            Assert.That(cached.CacheHitRate, Is.GreaterThan(0.60d));
            Assert.That(cached.TotalCacheStores, Is.GreaterThan(0));
            Assert.That(reference.FinalDesiredVelocities.Length, Is.EqualTo(cached.FinalDesiredVelocities.Length));

            float maxDesiredDelta = 0f;
            for (int i = 0; i < reference.FinalDesiredVelocities.Length; i++)
            {
                float delta = Vector2.Distance(reference.FinalDesiredVelocities[i], cached.FinalDesiredVelocities[i]);
                if (delta > maxDesiredDelta)
                {
                    maxDesiredDelta = delta;
                }
            }

            Assert.That(maxDesiredDelta, Is.LessThanOrEqualTo(0.01f));

            string traceJsonl = BuildTraceJsonl(cached);
            string battleReport = BuildBattleReport(cached, maxDesiredDelta);
            string pathMmd = BuildPathMermaid();

            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), battleReport);
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), pathMmd);
        }

        [Test]
        public void Navigation2DWorld_Clear_ResetsTemporalCoherenceInstrumentation()
        {
            using var world = new Navigation2DWorld(new Navigation2DWorldSettings(maxAgents: 8, cellSizeCm: Fix64.FromInt(100)));

            world.BeginSteeringFrame(steeringTick: 7, cacheEnabled: true, stableWorldFrame: true);
            world.RecordSteeringCacheLookup(hit: true);
            world.RecordSteeringCacheStore();

            Assert.That(world.SteeringFrameTick, Is.EqualTo(7));
            Assert.That(world.SteeringCacheFrameEnabled, Is.True);
            Assert.That(world.SteeringCacheLookupsFrame, Is.EqualTo(1));
            Assert.That(world.SteeringCacheHitsFrame, Is.EqualTo(1));
            Assert.That(world.SteeringCacheStoresFrame, Is.EqualTo(1));

            world.Clear();

            Assert.That(world.SteeringFrameTick, Is.EqualTo(0));
            Assert.That(world.SteeringCacheFrameEnabled, Is.False);
            Assert.That(world.SteeringCacheLookupsFrame, Is.EqualTo(0));
            Assert.That(world.SteeringCacheHitsFrame, Is.EqualTo(0));
            Assert.That(world.SteeringCacheStoresFrame, Is.EqualTo(0));
            Assert.That(world.SteeringCacheLookupsTotal, Is.EqualTo(0));
            Assert.That(world.SteeringCacheHitsTotal, Is.EqualTo(0));
            Assert.That(world.SteeringCacheStoresTotal, Is.EqualTo(0));
        }

        private static TemporalScenarioResult RunScenario(bool cacheEnabled)
        {
            using var world = World.Create();
            using var runtime = CreateRuntime(cacheEnabled);
            var steering = new Navigation2DSteeringSystem2D(world, runtime);
            var agents = CreateOpposingCrowd(world);
            var tickStats = new List<TemporalTickStats>(SimulationTicks);

            for (int tick = 0; tick < SimulationTicks; tick++)
            {
                steering.Update(DeltaTime);
                tickStats.Add(new TemporalTickStats(
                    Tick: tick + 1,
                    Lookups: runtime.AgentSoA.SteeringCacheLookupsFrame,
                    Hits: runtime.AgentSoA.SteeringCacheHitsFrame,
                    Stores: runtime.AgentSoA.SteeringCacheStoresFrame));
            }

            var finalDesired = new Vector2[agents.Length];
            for (int i = 0; i < agents.Length; i++)
            {
                finalDesired[i] = world.Get<NavDesiredVelocity2D>(agents[i]).ValueCmPerSec.ToVector2();
            }

            long totalLookups = 0;
            long totalHits = 0;
            long totalStores = 0;
            foreach (var stat in tickStats)
            {
                totalLookups += stat.Lookups;
                totalHits += stat.Hits;
                totalStores += stat.Stores;
            }

            return new TemporalScenarioResult(
                AgentCount: agents.Length,
                TickStats: tickStats,
                FinalDesiredVelocities: finalDesired,
                TotalCacheLookups: totalLookups,
                TotalCacheHits: totalHits,
                TotalCacheStores: totalStores);
        }

        private static Entity[] CreateOpposingCrowd(World world)
        {
            var entities = new Entity[AgentsPerTeam * 2];
            int index = 0;
            const int rows = 4;
            const int cols = 4;
            const float spacingCm = 80f;
            const float sideOffsetCm = 180f;
            const float goalOffsetCm = 1500f;

            for (int row = 0; row < rows; row++)
            {
                float y = (row - 1.5f) * spacingCm;
                for (int col = 0; col < cols; col++)
                {
                    float depth = col * spacingCm;
                    entities[index++] = CreateAgent(world, new Vector2(-sideOffsetCm - depth, y), new Vector2(goalOffsetCm, y));
                    entities[index++] = CreateAgent(world, new Vector2(sideOffsetCm + depth, y), new Vector2(-goalOffsetCm, y));
                }
            }

            return entities;
        }

        private static Entity CreateAgent(World world, Vector2 position, Vector2 goal)
        {
            return world.Create(
                new NavAgent2D(),
                new NavGoal2D
                {
                    Kind = NavGoalKind2D.Point,
                    TargetCm = Fix64Vec2.FromVector2(goal),
                    RadiusCm = Fix64.Zero,
                },
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(220),
                    MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                    RadiusCm = Fix64.FromInt(30),
                    NeighborDistCm = Fix64.FromInt(240),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 8,
                },
                new Position2D { Value = Fix64Vec2.FromVector2(position) },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });
        }

        private static Navigation2DRuntime CreateRuntime(bool cacheEnabled)
        {
            var config = new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 64,
                FlowIterationsPerTick = 0,
                Steering = new Navigation2DSteeringConfig
                {
                    Mode = Navigation2DAvoidanceMode.Hybrid,
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
                        DenseNeighborThreshold = 4,
                        MinSpeedForOrcaCmPerSec = 0,
                        MinOpposingNeighborsForOrca = 1,
                        OpposingVelocityDotThreshold = -0.2f,
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
                    },
                    TemporalCoherence = new Navigation2DSteeringTemporalCoherenceConfig
                    {
                        Enabled = cacheEnabled,
                        RequireSteadyStateWorld = false,
                        MaxReuseTicks = 12,
                        PositionToleranceCm = 2,
                        VelocityToleranceCmPerSec = 4,
                        PreferredVelocityToleranceCmPerSec = 4,
                        NeighborPositionQuantizationCm = 8,
                        NeighborVelocityQuantizationCmPerSec = 8,
                    }
                }
            };

            return new Navigation2DRuntime(config, gridCellSizeCm: 100, loadedChunks: null)
            {
                FlowEnabled = false,
                FlowIterationsPerTick = 0,
            };
        }

        private static string BuildTraceJsonl(TemporalScenarioResult result)
        {
            var lines = new List<string>(result.TickStats.Count);
            foreach (var tick in result.TickStats)
            {
                double hitRate = tick.Lookups > 0 ? (double)tick.Hits / tick.Lookups : 0d;
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"nav2d-temporal-{tick.Tick:000}",
                    step = "steering_tick",
                    tick = tick.Tick,
                    agents = result.AgentCount,
                    cache_lookups = tick.Lookups,
                    cache_hits = tick.Hits,
                    cache_stores = tick.Stores,
                    cache_hit_rate = Math.Round(hitRate, 4),
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(TemporalScenarioResult result, float maxDesiredDelta)
        {
            var timeline = new StringBuilder();
            foreach (var tick in result.TickStats)
            {
                double hitRate = tick.Lookups > 0 ? (double)tick.Hits / tick.Lookups : 0d;
                timeline.AppendLine($"- [T+{tick.Tick:000}] Tick#{tick.Tick} | Agents={result.AgentCount} | CacheLookups={tick.Lookups} | CacheHits={tick.Hits} | CacheStores={tick.Stores} | HitRate={hitRate:P1}");
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-steering-temporal-coherence");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: keep dense 2D crowds responsive without changing steering output.");
            sb.AppendLine("- Gameplay domain: local avoidance / steering hot path.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: synthetic static opposing crowd, no flow field.");
            sb.AppendLine("- Clock profile: fixed `1/60s`, `6` steering ticks.");
            sb.AppendLine($"- Initial entities: `{result.AgentCount}` nav agents with point goals and explicit kinematics.");
            sb.AppendLine("- Config source: `Navigation2D.Steering.TemporalCoherence` through `Navigation2DConfig.CloneValidated()`.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Run the same dense crowd once with temporal coherence disabled.");
            sb.AppendLine("2. Run it again with temporal coherence explicitly enabled.");
            sb.AppendLine("3. Record per-tick cache lookups, hits, and stores.");
            sb.AppendLine("4. Compare final desired velocities against the cache-disabled reference.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: cache-enabled run records cache hits in steady-state ticks.");
            sb.AppendLine("- Failure branch condition: cache changes desired steering output or never reuses results.");
            sb.AppendLine("- Key metrics: cache lookups, hits, stores, hit rate, max desired-velocity delta vs reference.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-steering-temporal-coherence/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-steering-temporal-coherence/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-steering-temporal-coherence/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: explicit temporal coherence reuses steady-state steering solves while preserving final desired velocities.");
            sb.AppendLine($"- reason: total cache hit rate reached `{result.CacheHitRate:P1}` with max desired delta `{maxDesiredDelta:F4}` cm/s vs the cache-disabled reference.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- agent count: `{result.AgentCount}`");
            sb.AppendLine($"- total cache lookups: `{result.TotalCacheLookups}`");
            sb.AppendLine($"- total cache hits: `{result.TotalCacheHits}`");
            sb.AppendLine($"- total cache stores: `{result.TotalCacheStores}`");
            sb.AppendLine($"- cache hit rate: `{result.CacheHitRate:P1}`");
            sb.AppendLine($"- max desired delta vs reference: `{maxDesiredDelta:F4}` cm/s");
            sb.AppendLine("- reusable wiring: config via `Navigation2D.Steering.TemporalCoherence`, runtime via `Navigation2DWorld`, HUD via `ScreenOverlayBuffer`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load explicit Steering.TemporalCoherence config] --> B[Spawn dense opposing crowd]",
                "    B --> C[Tick 1 cold solve and populate cache]",
                "    C --> D{Steady-state tick?}",
                "    D -->|yes| E[Reuse cached desired velocity]",
                "    D -->|no| F[Run full steering solve]",
                "    E --> G[Compare final desired velocity with reference run]",
                "    F --> G",
                "    G --> H[Write battle-report + trace + path]"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private readonly record struct TemporalTickStats(int Tick, int Lookups, int Hits, int Stores);

        private readonly record struct TemporalScenarioResult(
            int AgentCount,
            IReadOnlyList<TemporalTickStats> TickStats,
            Vector2[] FinalDesiredVelocities,
            long TotalCacheLookups,
            long TotalCacheHits,
            long TotalCacheStores)
        {
            public double CacheHitRate => TotalCacheLookups > 0 ? (double)TotalCacheHits / TotalCacheLookups : 0d;
        }
    }
}
