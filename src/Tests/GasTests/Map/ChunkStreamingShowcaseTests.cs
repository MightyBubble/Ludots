using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
using NUnit.Framework;
using RoadNetworkShowcaseMod.Runtime;
using Ludots.Tests.TestCommon;

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

            FocusLandmark(
                engine,
                "EastGate",
                "Camera focused on East Gate chunk window.");
            Tick(engine, 6);
            timeline.Add(Capture(engine, "east_gate"));

            FocusLandmark(
                engine,
                "RedCapital",
                "Camera focused on Red Capital chunk window.");
            Tick(engine, 6);
            timeline.Add(Capture(engine, "red_capital"));

            ResetCamera(engine);
            Tick(engine, 6);
            timeline.Add(Capture(engine, "reset_center"));

            Assert.That(timeline[0].LoadedChunkCount, Is.EqualTo(25));
            Assert.That(timeline[0].LoadedNodeCount, Is.GreaterThan(100));
            Assert.That(timeline[0].LoadedSplineRibbonCount, Is.EqualTo(11));
            Assert.That(timeline[1].ChunkSignature, Is.Not.EqualTo(timeline[0].ChunkSignature));
            Assert.That(timeline[2].ChunkSignature, Is.Not.EqualTo(timeline[1].ChunkSignature));
            Assert.That(timeline[2].LoadedNodeCount, Is.GreaterThan(40));
            Assert.That(timeline[2].LoadedSplineRibbonCount, Is.GreaterThan(0));
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
                if (scenario.TryGetRoadRibbonChunk(chunkKey, out RoadNetworkScenarioDefinition.RoadRibbonSpec[]? chunkSplines))
                {
                    splineCount += chunkSplines.Length;
                }
            }

            return new ChunkSnapshot(
                step,
                engine.AuthorityCamera().State.TargetCm,
                board.LoadedChunksSource.ActiveChunkKeys.Count,
                board.GraphRuntime.CurrentGraph.NodeCount,
                splineCount,
                string.Join(",", board.LoadedChunksSource.ActiveChunkKeys.OrderBy(static chunkKey => chunkKey)));
        }

        private static void FocusLandmark(GameEngine engine, string landmarkName, string status)
        {
            object runtime = RequireRuntime(engine);
            Type runtimeType = runtime.GetType();
            object landmark = Enum.Parse(typeof(RoadNetworkScenarioDefinition.RoadLandmarkId), landmarkName, ignoreCase: false);
            MethodInfo? focusMethod = runtimeType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => string.Equals(method.Name, "TryFocusLandmark", StringComparison.Ordinal));

            Assert.That(focusMethod, Is.Not.Null, "Chunk streaming runtime must expose TryFocusLandmark for showcase control.");
            object? result = focusMethod!.Invoke(runtime, new object[] { engine, landmark, status });
            Assert.That(result, Is.EqualTo(true), $"Chunk streaming runtime failed to focus landmark '{landmarkName}'.");
        }

        private static void ResetCamera(GameEngine engine)
        {
            object runtime = RequireRuntime(engine);
            MethodInfo? resetMethod = runtime.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => string.Equals(method.Name, "TryResetCamera", StringComparison.Ordinal));
            Assert.That(resetMethod, Is.Not.Null, "Chunk streaming runtime must expose TryResetCamera for showcase control.");
            object? result = resetMethod!.Invoke(runtime, new object[] { engine });
            Assert.That(result, Is.EqualTo(true), "Chunk streaming runtime failed to reset the tactical camera.");
        }

        private static object RequireRuntime(GameEngine engine)
        {
            bool found = engine.GlobalContext.TryGetValue("ChunkStreamingShowcaseMod.Runtime", out object? runtime);
            Assert.That(found, Is.True, "Chunk streaming runtime should be registered into engine.GlobalContext.");
            Assert.That(runtime, Is.Not.Null, "Chunk streaming runtime instance should not be null.");
            return runtime!;
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
                sb.AppendLine($"- {snapshot.Step}: camera=`{snapshot.CameraTarget.X:0},{snapshot.CameraTarget.Y:0}` chunks=`{snapshot.LoadedChunkCount}` nodes=`{snapshot.LoadedNodeCount}` splines=`{snapshot.LoadedSplineRibbonCount}`");
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
                    splines = snapshot.LoadedSplineRibbonCount,
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
            int LoadedSplineRibbonCount,
            string ChunkSignature);
    }
}
