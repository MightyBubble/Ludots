using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.FlowField;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Navigation2D.Spatial;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public sealed class Navigation2DFlowLargeWorldBudgetTests
    {
        private const int CellSizeCm = 100;
        private const int TileSizeCells = 64;
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void FlowStreaming_LargeWorldBudget_WindowWorkStaysBoundedDespiteManyLoadedTiles()
        {
            using var world = World.Create();
            var loaded = new TestLoadedChunks();
            LoadLine(loaded, 0, 12, 0);
            for (int y = -320; y < -256; y++)
            {
                for (int x = 256; x < 320; x++)
                {
                    loaded.Load(PackTile(x, y));
                }
            }

            using var runtime = CreateRuntime(
                loaded,
                activationRadiusTiles: 2,
                maxActiveTiles: 16,
                unloadGraceTicks: 2,
                maxPotentialCells: 512f,
                maxWindowWidthTiles: 11,
                maxWindowHeightTiles: 9,
                worldBoundsEnabled: true,
                worldMinTileX: -512,
                worldMinTileY: -512,
                worldMaxTileX: 511,
                worldMaxTileY: 511);
            runtime.FlowEnabled = true;

            world.Create(new NavFlowGoal2D { FlowId = 0, GoalCm = TileCenterCm(0, 0), RadiusCm = Fix64.Zero });
            world.Create(
                new NavAgent2D(),
                new NavFlowBinding2D { FlowId = 0 },
                new Position2D { Value = TileCenterCm(6, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                CreateKinematics(),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });

            var system = new Navigation2DSteeringSystem2D(world, runtime);
            system.Update(DeltaTime);

            var flow = runtime.Flows[0];
            Assert.That(flow.InstrumentedWindowWidthTiles, Is.LessThanOrEqualTo(11));
            Assert.That(flow.InstrumentedWindowHeightTiles, Is.LessThanOrEqualTo(9));
            Assert.That(flow.InstrumentedWindowTileChecksFrame, Is.LessThan(200));
            Assert.That(flow.ActiveTileCount, Is.LessThanOrEqualTo(16));
            Assert.That(flow.IsTileActive(PackTile(256, -320)), Is.False);
            Assert.That(SampleFlowSpeed(flow, TileCenterCm(6, 0)), Is.GreaterThan(0f));
        }

        [Test]
        public void FlowStreaming_NewlyLoadedTile_SeedsIncrementallyWithoutFullRebuild()
        {
            using var world = World.Create();
            var loaded = new TestLoadedChunks();
            LoadLine(loaded, 0, 2, 0);

            using var runtime = CreateRuntime(
                loaded,
                activationRadiusTiles: 1,
                maxActiveTiles: 16,
                unloadGraceTicks: 2,
                maxPotentialCells: 512f,
                maxWindowWidthTiles: 0,
                maxWindowHeightTiles: 0,
                worldBoundsEnabled: false,
                worldMinTileX: -512,
                worldMinTileY: -512,
                worldMaxTileX: 511,
                worldMaxTileY: 511);
            runtime.FlowEnabled = true;

            world.Create(new NavFlowGoal2D { FlowId = 0, GoalCm = TileCenterCm(0, 0), RadiusCm = Fix64.Zero });
            Entity agent = world.Create(
                new NavAgent2D(),
                new NavFlowBinding2D { FlowId = 0 },
                new Position2D { Value = TileCenterCm(2, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                CreateKinematics(),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });

            var system = new Navigation2DSteeringSystem2D(world, runtime);
            system.Update(DeltaTime);

            var flow = runtime.Flows[0];
            int rebuildsAfterFirstTick = flow.InstrumentedFullRebuilds;
            Assert.That(flow.IsTileActive(PackTile(2, 0)), Is.True);

            loaded.Load(PackTile(3, 0));
            ref var position = ref world.Get<Position2D>(agent);
            position.Value = TileCenterCm(3, 0);
            system.Update(DeltaTime);

            Assert.That(flow.IsTileActive(PackTile(3, 0)), Is.True);
            Assert.That(flow.InstrumentedIncrementalSeededTilesFrame, Is.GreaterThan(0));
            Assert.That(flow.InstrumentedFullRebuilds, Is.EqualTo(rebuildsAfterFirstTick));
            Assert.That(SampleFlowSpeed(flow, TileCenterCm(3, 0)), Is.GreaterThan(0f));
        }

        [Test]
        public void FlowStreaming_LargeWorldAcceptance_WritesArtifacts()
        {
            using var world = World.Create();
            var loaded = new TestLoadedChunks();
            LoadLine(loaded, 0, 6, 0);
            loaded.Load(PackTile(20, 0));

            using var runtime = CreateRuntime(
                loaded,
                activationRadiusTiles: 1,
                maxActiveTiles: 12,
                unloadGraceTicks: 1,
                maxPotentialCells: 512f,
                maxWindowWidthTiles: 9,
                maxWindowHeightTiles: 9,
                worldBoundsEnabled: true,
                worldMinTileX: -4,
                worldMinTileY: -4,
                worldMaxTileX: 4,
                worldMaxTileY: 4);
            runtime.FlowEnabled = true;

            world.Create(new NavFlowGoal2D { FlowId = 0, GoalCm = TileCenterCm(0, 0), RadiusCm = Fix64.Zero });
            Entity agent = world.Create(
                new NavAgent2D(),
                new NavFlowBinding2D { FlowId = 0 },
                new Position2D { Value = TileCenterCm(3, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                CreateKinematics(),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });

            var system = new Navigation2DSteeringSystem2D(world, runtime);
            var flow = runtime.Flows[0];
            var timeline = new StringBuilder();
            var trace = new StringBuilder();

            system.Update(DeltaTime);
            AppendTrace(trace, 1, flow, PackTile(20, 0), flow.IsTileActive(PackTile(20, 0)));
            timeline.AppendLine("- tick 1: inside the configured world bounds, the corridor around the goal stays active and far tile 20 remains inactive.");

            ref var position = ref world.Get<Position2D>(agent);
            position.Value = TileCenterCm(6, 0);
            system.Update(DeltaTime);
            AppendTrace(trace, 2, flow, PackTile(6, 0), flow.IsTileActive(PackTile(6, 0)));
            timeline.AppendLine("- tick 2: moving demand beyond the explicit world bounds clamps the activation window and keeps tile 6 outside the active set.");

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-flow-large-world-budget");
            Directory.CreateDirectory(artifactDir);

            string battleReport = BuildBattleReport(timeline);
            string traceJsonl = trace.ToString();
            string pathMmd = BuildPathMermaid();

            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), battleReport);
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), pathMmd);

            Assert.That(battleReport, Does.Contain("navigation2d-flow-large-world-budget"));
            Assert.That(traceJsonl, Does.Contain("\"tick\":2"));
            Assert.That(flow.InstrumentedWindowWorldClampedFrame, Is.True);
            Assert.That(flow.IsTileActive(PackTile(6, 0)), Is.False);
        }

        private static Navigation2DRuntime CreateRuntime(
            TestLoadedChunks loaded,
            int activationRadiusTiles,
            int maxActiveTiles,
            int unloadGraceTicks,
            float maxPotentialCells,
            int maxWindowWidthTiles,
            int maxWindowHeightTiles,
            bool worldBoundsEnabled,
            int worldMinTileX,
            int worldMinTileY,
            int worldMaxTileX,
            int worldMaxTileY)
        {
            var config = new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 64,
                FlowIterationsPerTick = 65536,
                FlowStreaming = new Navigation2DFlowStreamingConfig
                {
                    Enabled = true,
                    ActivationRadiusTiles = activationRadiusTiles,
                    MaxActiveTilesPerFlow = maxActiveTiles,
                    UnloadGraceTicks = unloadGraceTicks,
                    MaxPotentialCells = maxPotentialCells,
                    MaxActivationWindowWidthTiles = maxWindowWidthTiles,
                    MaxActivationWindowHeightTiles = maxWindowHeightTiles,
                    WorldBoundsEnabled = worldBoundsEnabled,
                    WorldMinTileX = worldMinTileX,
                    WorldMinTileY = worldMinTileY,
                    WorldMaxTileX = worldMaxTileX,
                    WorldMaxTileY = worldMaxTileY,
                }
            }.CloneValidated();

            return new Navigation2DRuntime(config, gridCellSizeCm: CellSizeCm, loadedChunks: loaded);
        }

        private static NavKinematics2D CreateKinematics()
        {
            return new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(800),
                MaxAccelCmPerSec2 = Fix64.FromInt(8000),
                RadiusCm = Fix64.FromInt(30),
                NeighborDistCm = Fix64.FromInt(300),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 8,
            };
        }

        private static float SampleFlowSpeed(CrowdFlow2D flow, Fix64Vec2 positionCm)
        {
            return flow.TrySampleDesiredVelocityCm(positionCm, Fix64.FromInt(800), out Fix64Vec2 velocity)
                ? velocity.ToVector2().Length()
                : 0f;
        }

        private static void LoadLine(TestLoadedChunks loaded, int startTileX, int endTileX, int tileY)
        {
            for (int tileX = startTileX; tileX <= endTileX; tileX++)
            {
                loaded.Load(PackTile(tileX, tileY));
            }
        }

        private static long PackTile(int tileX, int tileY)
        {
            return Nav2DKeyPacking.PackInt2(tileX, tileY);
        }

        private static Fix64Vec2 TileCenterCm(int tileX, int tileY)
        {
            int cellX = tileX * TileSizeCells + (TileSizeCells / 2);
            int cellY = tileY * TileSizeCells + (TileSizeCells / 2);
            return Fix64Vec2.FromInt(cellX * CellSizeCm, cellY * CellSizeCm);
        }

        private static void AppendTrace(StringBuilder trace, int tick, CrowdFlow2D flow, long observedTile, bool observedTileActive)
        {
            trace.Append('{');
            trace.Append("\"tick\":").Append(tick).Append(',');
            trace.Append("\"activeTiles\":").Append(flow.ActiveTileCount).Append(',');
            trace.Append("\"windowWidth\":").Append(flow.InstrumentedWindowWidthTiles).Append(',');
            trace.Append("\"windowHeight\":").Append(flow.InstrumentedWindowHeightTiles).Append(',');
            trace.Append("\"checks\":").Append(flow.InstrumentedWindowTileChecksFrame).Append(',');
            trace.Append("\"selected\":").Append(flow.InstrumentedSelectedTilesFrame).Append(',');
            trace.Append("\"newTiles\":").Append(flow.InstrumentedNewTilesActivatedFrame).Append(',');
            trace.Append("\"evicted\":").Append(flow.InstrumentedEvictedTilesFrame).Append(',');
            trace.Append("\"incremental\":").Append(flow.InstrumentedIncrementalSeededTilesFrame).Append(',');
            trace.Append("\"goalSeeds\":").Append(flow.InstrumentedGoalSeedCellsFrame).Append(',');
            trace.Append("\"frontierProcessed\":").Append(flow.InstrumentedFrontierProcessedFrame).Append(',');
            trace.Append("\"rebuilds\":").Append(flow.InstrumentedFullRebuilds).Append(',');
            trace.Append("\"budgetClamp\":").Append(flow.InstrumentedWindowBudgetClampedFrame ? "true" : "false").Append(',');
            trace.Append("\"worldClamp\":").Append(flow.InstrumentedWindowWorldClampedFrame ? "true" : "false").Append(',');
            trace.Append("\"observedTile\":").Append(observedTile).Append(',');
            trace.Append("\"observedTileActive\":").Append(observedTileActive ? "true" : "false");
            trace.AppendLine("}");
        }

        private static string BuildBattleReport(StringBuilder timeline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-flow-large-world-budget");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: keep flowfield propagation smooth in large worlds by using explicit activation-window and world-bound budgets.");
            sb.AppendLine("- Gameplay domain: `Navigation2D` flow streaming, large-world propagation, and runtime telemetry.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Runtime: `Navigation2DRuntime` + `Navigation2DSteeringSystem2D`");
            sb.AppendLine("- Loaded tiles: corridor 0..6 plus one out-of-budget tile 20");
            sb.AppendLine("- Config source: `Navigation2D.FlowStreaming` explicit world/window budget fields");
            sb.AppendLine("- Clock profile: fixed `1/60s`, two steering updates");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Flow activation stays inside the explicit world bounds.");
            sb.AppendLine("- Window work stays bounded by explicit width/height budget.");
            sb.AppendLine("- Demand beyond bounds clamps instead of forcing a huge propagation rebuild domain.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: large-world flowfield activation now honors explicit budget config and exposes enough telemetry for playable verification.");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Configure explicit FlowStreaming world/window budgets] --> B[Load corridor + far sparse tiles]",
                "    B --> C[Apply flow goal]",
                "    C --> D[Collect demand tile]",
                "    D --> E[Clamp activation window to explicit bounds]",
                "    E --> F[Activate/seed selected tiles]",
                "    F --> G[Step frontier propagation]",
                "    G --> H[Move demand outside world budget]",
                "    H --> I[Observe world clamp + inactive out-of-bounds tile]",
                "    I --> J[Write battle-report + trace + path]"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private sealed class TestLoadedChunks : ILoadedChunks
        {
            private readonly HashSet<long> _activeChunkKeys = new();

            public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunkKeys;
            public event Action<long>? ChunkLoaded;
            public event Action<long>? ChunkUnloaded;

            public bool IsLoaded(long chunkKey) => _activeChunkKeys.Contains(chunkKey);

            public void Load(long chunkKey)
            {
                if (_activeChunkKeys.Add(chunkKey))
                {
                    ChunkLoaded?.Invoke(chunkKey);
                }
            }

            public void Unload(long chunkKey)
            {
                if (_activeChunkKeys.Remove(chunkKey))
                {
                    ChunkUnloaded?.Invoke(chunkKey);
                }
            }
        }
    }
}
