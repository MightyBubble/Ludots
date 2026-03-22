using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Board;
using NUnit.Framework;
using RoadNetworkShowcaseMod.Runtime;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ChunkStreamingShowcaseTests
    {
        private const string ChunkShowcaseMapId = "chunk_streaming_showcase";

        [Test]
        public void ChunkStreamingShowcase_EngineStreamsChunkWindows_AndWritesArtifacts()
        {
            using var engine = CreateChunkShowcaseEngine();
            engine.LoadMap(ChunkShowcaseMapId);

            var timeline = new List<ChunkSnapshot>(capacity: 4);
            Tick(engine, 6);
            timeline.Add(Capture(engine, "start"));

            ApplyCamera(engine, new Vector2(9000f, 0f));
            Tick(engine, 6);
            timeline.Add(Capture(engine, "east_gate"));

            ApplyCamera(engine, new Vector2(18000f, 0f));
            Tick(engine, 6);
            timeline.Add(Capture(engine, "red_capital"));

            ApplyCamera(engine, Vector2.Zero);
            Tick(engine, 6);
            timeline.Add(Capture(engine, "reset_center"));

            Assert.That(timeline[0].LoadedChunkCount, Is.EqualTo(25));
            Assert.That(timeline[0].LoadedNodeCount, Is.GreaterThan(100));
            Assert.That(timeline[0].LoadedRoadSplineCount, Is.EqualTo(11));
            Assert.That(timeline[1].ChunkSignature, Is.Not.EqualTo(timeline[0].ChunkSignature));
            Assert.That(timeline[2].ChunkSignature, Is.Not.EqualTo(timeline[1].ChunkSignature));
            Assert.That(timeline[2].LoadedNodeCount, Is.GreaterThan(40));
            Assert.That(timeline[2].LoadedRoadSplineCount, Is.GreaterThan(0));
            Assert.That(timeline[3].ChunkSignature, Is.EqualTo(timeline[0].ChunkSignature));

            string artifactDir = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "chunk_streaming_showcase");
            Directory.CreateDirectory(artifactDir);
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline));
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTrace(timeline));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPath());
            File.WriteAllText(Path.Combine(artifactDir, "summary.json"), BuildSummary(timeline));
        }

        private static ChunkSnapshot Capture(GameEngine engine, string step)
        {
            var board = (NodeGraphBoard)engine.CurrentMapSession!.PrimaryBoard;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(board.LoadedChunksSource.ChunkSizeCm);
            int splineCount = 0;
            foreach (long chunkKey in board.LoadedChunksSource.ActiveChunkKeys)
            {
                if (scenario.TryGetRoadSplineChunk(chunkKey, out RoadNetworkScenarioDefinition.RoadSplineSpec[]? chunkSplines))
                {
                    splineCount += chunkSplines.Length;
                }
            }

            return new ChunkSnapshot(
                step,
                engine.GameSession.Camera.State.TargetCm,
                board.LoadedChunksSource.ActiveChunkKeys.Count,
                board.GraphRuntime.CurrentGraph.NodeCount,
                splineCount,
                string.Join(",", board.LoadedChunksSource.ActiveChunkKeys));
        }

        private static void ApplyCamera(GameEngine engine, Vector2 targetCm)
        {
            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "Camera.Profile.Tactical",
                TargetCm = targetCm
            });
        }

        private static void Tick(GameEngine engine, int count)
        {
            for (int i = 0; i < count; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static GameEngine CreateChunkShowcaseEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(repoRoot, "mods", "showcases", "chunk_streaming", "ChunkStreamingShowcaseMod"),
            };

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.Start();
            return engine;
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Repository root not found from test directory.");
        }

        private static string BuildBattleReport(IReadOnlyList<ChunkSnapshot> timeline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: chunk_streaming_showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Validate a standalone showcase mod for chunk window streaming without move-order gameplay coupling.");
            sb.AppendLine("- Acceptance focus: moving the camera between authored landmarks changes loaded chunk signatures, node counts, and loaded road spline batches.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            foreach (ChunkSnapshot snapshot in timeline)
            {
                sb.AppendLine($"- {snapshot.Step}: camera=`{snapshot.CameraTarget.X:0},{snapshot.CameraTarget.Y:0}` chunks=`{snapshot.LoadedChunkCount}` nodes=`{snapshot.LoadedNodeCount}` splines=`{snapshot.LoadedRoadSplineCount}`");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: the chunk showcase exposes a readable camera-driven chunk window with road spline batches that shift as the camera moves.");
            return sb.ToString();
        }

        private static string BuildTrace(IReadOnlyList<ChunkSnapshot> timeline)
        {
            var lines = new List<string>(timeline.Count);
            foreach (ChunkSnapshot snapshot in timeline)
            {
                lines.Add(JsonSerializer.Serialize(new
                {
                    step = snapshot.Step,
                    camera = new { x = snapshot.CameraTarget.X, y = snapshot.CameraTarget.Y },
                    chunks = snapshot.LoadedChunkCount,
                    nodes = snapshot.LoadedNodeCount,
                    splines = snapshot.LoadedRoadSplineCount,
                    signature = snapshot.ChunkSignature
                }));
            }

            return string.Join(System.Environment.NewLine, lines) + System.Environment.NewLine;
        }

        private static string BuildPath()
        {
            return string.Join(System.Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load chunk_streaming_showcase] --> B[Center camera on central crossing]",
                "    B --> C[Capture center chunk window]",
                "    C --> D[Move camera to East Gate]",
                "    D --> E[Chunk signature changes and window follows camera]",
                "    E --> F[Move camera to Red Capital]",
                "    F --> G[Capture far-east chunk window]",
                "    G --> H[Reset camera to center and restore original window]"
            }) + System.Environment.NewLine;
        }

        private static string BuildSummary(IReadOnlyList<ChunkSnapshot> timeline)
        {
            return JsonSerializer.Serialize(new
            {
                scenario = "chunk_streaming_showcase",
                snapshots = timeline
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private readonly record struct ChunkSnapshot(
            string Step,
            Vector2 CameraTarget,
            int LoadedChunkCount,
            int LoadedNodeCount,
            int LoadedRoadSplineCount,
            string ChunkSignature);
    }
}
