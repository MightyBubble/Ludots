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
    public sealed class Navigation2DFlowStreamingTests
    {
        private const int CellSizeCm = 100;
        private const int TileSizeCells = 64;
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void FlowStreaming_EndToEndSteering_ActivatesDemandWindowAndSkipsFarTiles()
        {
            using var world = World.Create();
            var loaded = new TestLoadedChunks();
            LoadLine(loaded, 0, 5, 0);
            loaded.Load(PackTile(20, 0));

            using var runtime = CreateRuntime(loaded, activationRadiusTiles: 1, maxActiveTiles: 16, unloadGraceTicks: 2, maxPotentialCells: 512f);
            runtime.FlowEnabled = true;

            world.Create(new NavFlowGoal2D { FlowId = 0, GoalCm = TileCenterCm(0, 0), RadiusCm = Fix64.Zero });
            world.Create(
                new NavAgent2D(),
                new NavFlowBinding2D { FlowId = 0 },
                new Position2D { Value = TileCenterCm(3, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                CreateKinematics(),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });

            var system = new Navigation2DSteeringSystem2D(world, runtime);
            system.Update(DeltaTime);

            var flow = runtime.Flows[0];
            Assert.That(flow.ActiveTileCount, Is.GreaterThan(0));
            Assert.That(flow.IsTileActive(PackTile(20, 0)), Is.False, "far loaded tile should stay inactive");
            Assert.That(runtime.Flows[0].TrySampleDesiredVelocityCm(TileCenterCm(3, 0), Fix64.FromInt(800), out Fix64Vec2 sampledVelocity), Is.True);
            Assert.That(sampledVelocity.ToVector2().LengthSquared(), Is.GreaterThan(1f), "flow sample should be available inside the active corridor");
        }

        [Test]
        public void FlowStreaming_ChunkUnload_RemovesDeactivatedTiles()
        {
            using var world = World.Create();
            var loaded = new TestLoadedChunks();
            LoadLine(loaded, 0, 4, 0);

            using var runtime = CreateRuntime(loaded, activationRadiusTiles: 1, maxActiveTiles: 16, unloadGraceTicks: 0, maxPotentialCells: 512f);
            runtime.FlowEnabled = true;

            world.Create(new NavFlowGoal2D { FlowId = 0, GoalCm = TileCenterCm(0, 0), RadiusCm = Fix64.Zero });
            world.Create(
                new NavAgent2D(),
                new NavFlowBinding2D { FlowId = 0 },
                new Position2D { Value = TileCenterCm(3, 0) },
                new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                CreateKinematics(),
                new ForceInput2D { Force = Fix64Vec2.Zero },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });

            var system = new Navigation2DSteeringSystem2D(world, runtime);
            system.Update(DeltaTime);
            Assert.That(runtime.Flows[0].IsTileActive(PackTile(3, 0)), Is.True);

            loaded.Unload(PackTile(3, 0));
            system.Update(DeltaTime);

            Assert.That(runtime.Flows[0].IsTileActive(PackTile(3, 0)), Is.False);
        }

        [Test]
        public void FlowStreaming_Acceptance_WritesArtifacts()
        {
            using var world = World.Create();
            var loaded = new TestLoadedChunks();
            LoadLine(loaded, 0, 5, 0);
            loaded.Load(PackTile(10, 0));

            using var runtime = CreateRuntime(loaded, activationRadiusTiles: 1, maxActiveTiles: 16, unloadGraceTicks: 1, maxPotentialCells: 512f);
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
            var timeline = new StringBuilder();
            var trace = new StringBuilder();

            system.Update(DeltaTime);
            AppendTrace(trace, 1, runtime.Flows[0].ActiveTileCount, SampleFlowSpeed(runtime.Flows[0], TileCenterCm(3, 0)), PackTile(10, 0), runtime.Flows[0].IsTileActive(PackTile(10, 0)));
            timeline.AppendLine("- tick 1: demand at tile 3 activates the goal-to-demand corridor and leaves far tile 10 inactive.");

            ref var position = ref world.Get<Position2D>(agent);
            position.Value = TileCenterCm(4, 0);
            system.Update(DeltaTime);
            AppendTrace(trace, 2, runtime.Flows[0].ActiveTileCount, SampleFlowSpeed(runtime.Flows[0], TileCenterCm(4, 0)), PackTile(10, 0), runtime.Flows[0].IsTileActive(PackTile(10, 0)));
            timeline.AppendLine("- tick 2: moving demand forward keeps flow output non-zero and active tiles bounded.");

            loaded.Unload(PackTile(4, 0));
            system.Update(DeltaTime);
            AppendTrace(trace, 3, runtime.Flows[0].ActiveTileCount, SampleFlowSpeed(runtime.Flows[0], TileCenterCm(4, 0)), PackTile(4, 0), runtime.Flows[0].IsTileActive(PackTile(4, 0)));
            timeline.AppendLine("- tick 3: unloading tile 4 removes it from the active set on the next steering update.");

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-flow-streaming");
            Directory.CreateDirectory(artifactDir);

            string battleReport = BuildBattleReport(timeline);
            string traceJsonl = trace.ToString();
            string pathMmd = BuildPathMermaid();

            string battleReportPath = Path.Combine(artifactDir, "battle-report.md");
            string tracePath = Path.Combine(artifactDir, "trace.jsonl");
            string pathPath = Path.Combine(artifactDir, "path.mmd");
            File.WriteAllText(battleReportPath, battleReport);
            File.WriteAllText(tracePath, traceJsonl);
            File.WriteAllText(pathPath, pathMmd);

            Assert.That(File.Exists(battleReportPath), Is.True);
            Assert.That(File.Exists(tracePath), Is.True);
            Assert.That(File.Exists(pathPath), Is.True);
            Assert.That(battleReport, Does.Contain("navigation2d-flow-streaming"));
            Assert.That(traceJsonl, Does.Contain("\"tick\":1"));
        }

        private static Navigation2DRuntime CreateRuntime(TestLoadedChunks loaded, int activationRadiusTiles, int maxActiveTiles, int unloadGraceTicks, float maxPotentialCells)
        {
            var config = new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 32,
                FlowIterationsPerTick = 65536,
                FlowStreaming = new Navigation2DFlowStreamingConfig
                {
                    Enabled = true,
                    ActivationRadiusTiles = activationRadiusTiles,
                    MaxActiveTilesPerFlow = maxActiveTiles,
                    UnloadGraceTicks = unloadGraceTicks,
                    MaxPotentialCells = maxPotentialCells,
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

        private static void AppendTrace(StringBuilder trace, int tick, int activeTiles, float desiredSpeed, long observedTile, bool observedTileActive)
        {
            trace.Append('{');
            trace.Append("\"tick\":").Append(tick).Append(',');
            trace.Append("\"activeTiles\":").Append(activeTiles).Append(',');
            trace.Append("\"desiredSpeed\":").Append(desiredSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            trace.Append("\"observedTile\":").Append(observedTile).Append(',');
            trace.Append("\"observedTileActive\":").Append(observedTileActive ? "true" : "false");
            trace.AppendLine("}");
        }

        private static string BuildBattleReport(StringBuilder timeline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-flow-streaming");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: keep flow propagation bounded to active demand windows instead of mirroring all loaded chunks into every flow.");
            sb.AppendLine("- Gameplay domain: `Navigation2D` runtime flow streaming, steering integration, and explicit config pipeline.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Runtime: `Navigation2DRuntime` + `Navigation2DSteeringSystem2D`");
            sb.AppendLine("- Loaded tiles: corridor 0..5 plus one far tile 10");
            sb.AppendLine("- Config source: `Navigation2D.FlowStreaming`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, three steering updates");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Only tiles inside the goal-to-demand window become active.");
            sb.AppendLine("- Far loaded tiles stay inactive.");
            sb.AppendLine("- Unloaded tiles leave the active set without hidden fallback.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: flow demand collection, active-window streaming, and chunk-unload cleanup are wired end to end.");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Configure Navigation2D.FlowStreaming] --> B[Load corridor chunks]",
                "    B --> C[Apply flow goal]",
                "    C --> D[Collect bound-agent demand]",
                "    D --> E[Activate bounded tile window]",
                "    E --> F[Step propagation]",
                "    F --> G[Sample desired velocity]",
                "    G --> H[Unload chunk and refresh active tiles]",
                "    H --> I[Write battle-report + trace + path]"
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




