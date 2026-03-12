using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Navigation2DPlaygroundMod;
using Navigation2DPlaygroundMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public sealed class Navigation2DPlaygroundPlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int AcceptanceAgentsPerTeam = 64;
        private const int AcceptanceSpawnBatch = 16;
        private const string CommandModeId = "Navigation2D.Playground.Mode.Command";
        private const string FollowModeId = "Navigation2D.Playground.Mode.Follow";
        private const string FollowCameraId = "Navigation2D.Playground.Camera.Follow";
        private const string ActiveViewModeIdKey = "CoreInputMod.ActiveViewModeId";
        private const string LeftMousePath = "<Mouse>/LeftButton";
        private const string RightMousePath = "<Mouse>/RightButton";

        private static readonly QueryDescription DynamicAgentsQuery = new QueryDescription()
            .WithAll<NavAgent2D, WorldPositionCm, NavGoal2D>()
            .WithNone<NavObstacle2D>();

        private static readonly QueryDescription BlockerQuery = new QueryDescription()
            .WithAll<NavObstacle2D, WorldPositionCm>();

        [Test]
        public void Navigation2DPlayground_PlayablePanelSelectionCommandSpawnAndViewModes_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-playground-playable");
            Directory.CreateDirectory(artifactDir);

            var snapshots = new List<PlayableSnapshot>();
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            LoadMap(engine, frameTimesMs);

            var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot;
            var navRuntime = engine.GetService(CoreServiceKeys.Navigation2DRuntime);
            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var mapping = (WorldScreenMapping)engine.GlobalContext["Tests.Navigation2DPlayground.Mapping"];
            var inputBackend = GetInputBackend(engine);

            Assert.That(uiRoot, Is.Not.Null);
            Assert.That(navRuntime, Is.Not.Null);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam), Is.EqualTo(AcceptanceAgentsPerTeam));
            Assert.That(engine.GetService(Navigation2DPlaygroundKeys.SpawnBatch), Is.EqualTo(AcceptanceSpawnBatch));
            Assert.That(engine.GetService(Navigation2DPlaygroundKeys.ScenarioName), Is.EqualTo("Pass Through"));

            UiScene mountedScene = uiRoot!.Scene ?? throw new InvalidOperationException("Playground panel should mount into UIRoot.");
            Tick(engine, 3, frameTimesMs);
            Assert.That(ReferenceEquals(mountedScene, uiRoot.Scene), Is.True,
                "Playground panel must keep the same mounted scene instance across presentation ticks.");

            CaptureSnapshot(engine, navRuntime!, overlay!, snapshots, frameTimesMs, "warmup");

            ScreenRect team0Bounds = GetTeamScreenBounds(engine, mapping, teamId: 0);
            Vector2 selectionMin = team0Bounds.Min - new Vector2(96f, 96f);
            Vector2 selectionMax = team0Bounds.Max + new Vector2(96f, 96f);
            int projectedHits = CountProjectedSelectableEntities(engine, mapping, selectionMin, selectionMax);
            TestContext.Progress.WriteLine($"team0 bounds: {team0Bounds.Min} -> {team0Bounds.Max}; projected hits={projectedHits}");
            Assert.That(projectedHits, Is.EqualTo(AcceptanceAgentsPerTeam));
            DragSelect(engine, inputBackend, selectionMin, selectionMax, frameTimesMs);
            TickUntil(engine, frameTimesMs, () => GetSelectedEntities(engine).Count == AcceptanceAgentsPerTeam, maxTicks: 12);

            Entity local = GetLocalPlayer(engine);
            ref var selection = ref engine.World.Get<SelectionBuffer>(local);
            Assert.That(selection.Count, Is.EqualTo(AcceptanceAgentsPerTeam), BuildSelectionDiagnostics(engine, mapping, selectionMin, selectionMax));
            Assert.That(ReadActiveModeId(engine), Is.EqualTo(CommandModeId));

            string selectedSceneText = ExtractUiSceneText(uiRoot.Scene!);
            Assert.That(selectedSceneText, Does.Contain("Selection Buffer"));
            Assert.That(selectedSceneText, Does.Contain("#"));
            CaptureSnapshot(engine, navRuntime, overlay, snapshots, frameTimesMs, "select_team0");

            var selectedEntities = GetSelectedEntities(engine);
            ClickWorld(engine, inputBackend, new Vector2(-2400f, 0f), frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            AssertSelectedGoalsNear(engine, selectedEntities, new Vector2(-2400f, 0f));
            CaptureSnapshot(engine, navRuntime, overlay, snapshots, frameTimesMs, "move_selected");

            int liveBeforeSpawn = engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal);
            PressButton(engine, inputBackend, "<Keyboard>/2", frameTimesMs);
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Tool: SpawnTeam0"));
            ClickWorld(
                engine,
                inputBackend,
                new Vector2(0f, 2600f),
                frameTimesMs,
                () => engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal) >= liveBeforeSpawn + AcceptanceSpawnBatch);
            Tick(engine, 2, frameTimesMs);
            Assert.That(engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal), Is.EqualTo(liveBeforeSpawn + AcceptanceSpawnBatch));
            Assert.That(CountAgentsNear(engine, centerCm: new Vector2(0f, 2600f), halfExtentCm: 240f), Is.EqualTo(AcceptanceSpawnBatch));
            CaptureSnapshot(engine, navRuntime, overlay, snapshots, frameTimesMs, "spawn_team0");

            int blockersBeforeSpawn = engine.GetService(Navigation2DPlaygroundKeys.BlockerCount);
            PressButton(engine, inputBackend, "<Keyboard>/4", frameTimesMs);
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Tool: SpawnBlocker"));
            ClickWorld(
                engine,
                inputBackend,
                new Vector2(1800f, -2200f),
                frameTimesMs,
                () => engine.GetService(Navigation2DPlaygroundKeys.BlockerCount) >= blockersBeforeSpawn + AcceptanceSpawnBatch);
            Tick(engine, 2, frameTimesMs);
            Assert.That(engine.GetService(Navigation2DPlaygroundKeys.BlockerCount), Is.EqualTo(blockersBeforeSpawn + AcceptanceSpawnBatch));
            Assert.That(CountBlockersNear(engine, new Vector2(1800f, -2200f), halfExtentCm: 240f), Is.EqualTo(AcceptanceSpawnBatch));
            CaptureSnapshot(engine, navRuntime, overlay, snapshots, frameTimesMs, "spawn_blocker");

            PressButton(engine, inputBackend, "<Keyboard>/f2", frameTimesMs);
            Tick(engine, 4, frameTimesMs);
            Assert.That(ReadActiveModeId(engine), Is.EqualTo(FollowModeId));
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(FollowCameraId));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            CaptureSnapshot(engine, navRuntime, overlay, snapshots, frameTimesMs, "follow_mode");

            PressButton(engine, inputBackend, "<Keyboard>/f1", frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            Assert.That(ReadActiveModeId(engine), Is.EqualTo(CommandModeId));
            CaptureSnapshot(engine, navRuntime, overlay, snapshots, frameTimesMs, "command_mode");
            AssertPanelHitTesting(uiRoot);

            string traceJsonl = BuildTraceJsonl(snapshots);
            string battleReport = BuildBattleReport(snapshots, frameTimesMs);
            string pathMmd = BuildPathMermaid();

            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), battleReport);
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), pathMmd);
        }

        private static GameEngine CreateEngine()
        {
            ResetPlaygroundStaticState();

            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(modsRoot, "LudotsCoreMod"),
                    Path.Combine(modsRoot, "CoreInputMod"),
                    Path.Combine(modsRoot, "Navigation2DPlaygroundMod")
                },
                assetsRoot);

            ConfigureAcceptanceDefaults(engine);
            InstallInput(engine);

            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            var mapping = new WorldScreenMapping(new Vector2(1200f, 540f), 0.03f);
            engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1920f, 1080f));
            engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)mapping);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)mapping);
            engine.GlobalContext["Tests.Navigation2DPlayground.Mapping"] = mapping;

            engine.Start();
            return engine;
        }

        private static void ConfigureAcceptanceDefaults(GameEngine engine)
        {
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            backend.SetMousePosition(new Vector2(1200f, 540f));
            engine.GlobalContext["Tests.Navigation2DPlayground.InputBackend"] = backend;
        }

        private static void LoadMap(GameEngine engine, List<double> frameTimesMs)
        {
            engine.LoadMap(engine.MergedConfig.StartupMapId);
            Tick(engine, 5, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        private static void AssertPanelHitTesting(UIRoot uiRoot)
        {
            bool handledOutside = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                X = 760f,
                Y = 420f
            });

            bool handledInside = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                X = 80f,
                Y = 80f
            });

            Assert.That(handledOutside, Is.False, "World clicks outside the playground card must pass through to gameplay input.");
            Assert.That(handledInside, Is.True, "Clicks on the playground card should remain UI-interactive.");
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            Navigation2DRuntime navRuntime,
            ScreenOverlayBuffer overlay,
            List<PlayableSnapshot> snapshots,
            IReadOnlyList<double> frameTimesMs,
            string step)
        {
            string[] selectedIds = GetSelectedEntities(engine)
                .Select(entity => $"#{entity.Id}")
                .ToArray();
            Vector2? primaryGoal = TryGetPrimarySelectedGoal(engine);
            UIRoot uiRoot = GetUiRoot(engine);
            string sceneText = ExtractUiSceneText(uiRoot.Scene!);
            string[] overlayHead = ExtractOverlayText(overlay).Take(4).ToArray();

            snapshots.Add(new PlayableSnapshot(
                Step: step,
                ScenarioName: engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown",
                ActiveModeId: ReadActiveModeId(engine),
                ActiveCameraId: engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId ?? "none",
                ToolMode: ResolveToolModeLabel(sceneText),
                AgentsPerTeam: engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam),
                LiveAgents: engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal),
                Blockers: engine.GetService(Navigation2DPlaygroundKeys.BlockerCount),
                SpawnBatch: engine.GetService(Navigation2DPlaygroundKeys.SpawnBatch),
                SelectedCount: selectedIds.Length,
                SelectedIds: selectedIds,
                PrimaryGoalX: primaryGoal?.X,
                PrimaryGoalY: primaryGoal?.Y,
                IsFollowing: engine.GameSession.Camera.State.IsFollowing,
                PanelSceneVersion: uiRoot.Scene?.Version ?? 0L,
                SceneTextHead: sceneText.Length > 280 ? sceneText.Substring(0, 280) : sceneText,
                OverlayHead: overlayHead,
                TickMs: frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d,
                FlowEnabled: navRuntime.FlowEnabled,
                FlowDebugEnabled: navRuntime.FlowDebugEnabled));
        }

        private static ScreenRect GetTeamScreenBounds(GameEngine engine, IScreenProjector projector, int teamId)
        {
            bool found = false;
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            foreach (ref var chunk in engine.World.Query(in DynamicAgentsQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                var goals = chunk.GetSpan<NavGoal2D>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!MatchesInitialPassThroughTeam(goals[i], teamId))
                    {
                        continue;
                    }

                    Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(positions[i].Value, yMeters: 0f));
                    min = Vector2.Min(min, screen);
                    max = Vector2.Max(max, screen);
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"No dynamic agents found for team {teamId}.");
            }

            return new ScreenRect(min, max);
        }

        private static void AssertSelectedGoalsNear(GameEngine engine, IReadOnlyList<Entity> selected, Vector2 targetCm)
        {
            Assert.That(selected.Count, Is.GreaterThan(0));
            const float formationToleranceCm = 640f;
            for (int i = 0; i < selected.Count; i++)
            {
                ref var goal = ref engine.World.Get<NavGoal2D>(selected[i]);
                Vector2 goalCm = goal.TargetCm.ToVector2();
                Assert.That(goalCm.X, Is.InRange(targetCm.X - formationToleranceCm, targetCm.X + formationToleranceCm));
                Assert.That(goalCm.Y, Is.InRange(targetCm.Y - formationToleranceCm, targetCm.Y + formationToleranceCm));
            }
        }

        private static int CountAgentsNear(GameEngine engine, Vector2 centerCm, float halfExtentCm)
        {
            int count = 0;
            foreach (ref var chunk in engine.World.Query(in DynamicAgentsQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Vector2 point = positions[i].Value.ToVector2();
                    if (MathF.Abs(point.X - centerCm.X) <= halfExtentCm &&
                        MathF.Abs(point.Y - centerCm.Y) <= halfExtentCm)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountProjectedSelectableEntities(GameEngine engine, IScreenProjector projector, Vector2 min, Vector2 max)
        {
            int count = 0;
            foreach (ref var chunk in engine.World.Query(in DynamicAgentsQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(positions[i].Value, yMeters: 0f));
                    if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static string BuildSelectionDiagnostics(GameEngine engine, IScreenProjector projector, Vector2 min, Vector2 max)
        {
            var lines = new List<string>();
            lines.Add($"rect=[{min.X:F1},{min.Y:F1}]..[{max.X:F1},{max.Y:F1}]");
            lines.Add("box-selectable:");
            foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<NavGoal2D, WorldPositionCm, CullState>().WithNone<NavObstacle2D>()))
            {
                var goals = chunk.GetSpan<NavGoal2D>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible)
                    {
                        continue;
                    }

                    Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(positions[i].Value, yMeters: 0f));
                    if (screen.X < min.X || screen.X > max.X || screen.Y < min.Y || screen.Y > max.Y)
                    {
                        continue;
                    }

                    Entity entity = chunk.Entity(i);
                    string lane = goals[i].TargetCm.X.ToFloat() >= 0f ? "goal+x" : "goal-x";
                    lines.Add($"  entity=#{entity.Id} lane={lane} world=({positions[i].Value.X},{positions[i].Value.Y}) screen=({screen.X:F1},{screen.Y:F1})");
                }
            }

            lines.Add("selection-buffer:");
            foreach (Entity entity in GetSelectedEntities(engine))
            {
                string goalLabel = "n/a";
                string worldLabel = "n/a";
                string screenLabel = "n/a";
                if (engine.World.Has<NavGoal2D>(entity))
                {
                    ref var goal = ref engine.World.Get<NavGoal2D>(entity);
                    goalLabel = goal.TargetCm.X.ToFloat() >= 0f ? "goal+x" : "goal-x";
                }
                if (engine.World.Has<WorldPositionCm>(entity))
                {
                    var position = engine.World.Get<WorldPositionCm>(entity);
                    Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
                    worldLabel = $"({position.Value.X},{position.Value.Y})";
                    screenLabel = $"({screen.X:F1},{screen.Y:F1})";
                }

                lines.Add($"  entity=#{entity.Id} lane={goalLabel} world={worldLabel} screen={screenLabel}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static int CountBlockersNear(GameEngine engine, Vector2 centerCm, float halfExtentCm)
        {
            int count = 0;
            foreach (ref var chunk in engine.World.Query(in BlockerQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Vector2 point = positions[i].Value.ToVector2();
                    if (MathF.Abs(point.X - centerCm.X) <= halfExtentCm &&
                        MathF.Abs(point.Y - centerCm.Y) <= halfExtentCm)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool MatchesInitialPassThroughTeam(in NavGoal2D goal, int teamId)
        {
            float goalX = goal.TargetCm.X.ToFloat();
            return teamId == 0 ? goalX >= 0f : goalX < 0f;
        }

        private static IReadOnlyList<Entity> GetSelectedEntities(GameEngine engine)
        {
            Entity local = GetLocalPlayer(engine);
            ref var selection = ref engine.World.Get<SelectionBuffer>(local);
            var entities = new List<Entity>(selection.Count);
            for (int i = 0; i < selection.Count; i++)
            {
                Entity entity = selection.Get(i);
                if (engine.World.IsAlive(entity))
                {
                    entities.Add(entity);
                }
            }

            return entities;
        }

        private static Vector2? TryGetPrimarySelectedGoal(GameEngine engine)
        {
            var selected = GetSelectedEntities(engine);
            if (selected.Count == 0)
            {
                return null;
            }

            Entity primary = selected[0];
            return engine.World.Has<NavGoal2D>(primary)
                ? engine.World.Get<NavGoal2D>(primary).TargetCm.ToVector2()
                : null;
        }

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   engine.World.IsAlive(local)
                ? local
                : throw new InvalidOperationException("LocalPlayerEntity is missing.");
        }

        private static UIRoot GetUiRoot(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot service is missing.");
        }

        private static string ReadActiveModeId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(ActiveViewModeIdKey, out var value) && value is string modeId
                ? modeId
                : "none";
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                engine.Tick(DeltaTime);
                frameTimesMs.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        private static void DragSelect(GameEngine engine, TestInputBackend backend, Vector2 from, Vector2 to, List<double> frameTimesMs)
        {
            backend.SetMousePosition(from);
            Tick(engine, 1, frameTimesMs);
            backend.SetButton(LeftMousePath, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetMousePosition(to);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(LeftMousePath, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void ClickWorld(
            GameEngine engine,
            TestInputBackend backend,
            Vector2 worldPointCm,
            List<double> frameTimesMs,
            Func<bool>? completion = null)
        {
            var mapping = (WorldScreenMapping)engine.GlobalContext["Tests.Navigation2DPlayground.Mapping"];
            Vector2 screen = mapping.WorldToScreen(WorldUnits.WorldCmToVisualMeters(new WorldCmInt2((int)worldPointCm.X, (int)worldPointCm.Y), yMeters: 0f));
            backend.SetMousePosition(screen);
            Tick(engine, 1, frameTimesMs);
            int attempts = completion == null ? 1 : 4;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                backend.SetButton(RightMousePath, true);
                Tick(engine, 2, frameTimesMs);
                backend.SetButton(RightMousePath, false);
                Tick(engine, 2, frameTimesMs);
                if (completion == null || completion())
                {
                    return;
                }
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxTicks)
        {
            if (predicate())
            {
                return;
            }

            for (int i = 0; i < maxTicks; i++)
            {
                Tick(engine, 1, frameTimesMs);
                if (predicate())
                {
                    return;
                }
            }
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static string ResolveToolModeLabel(string sceneText)
        {
            const string marker = "Tool: ";
            int start = sceneText.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return "Unknown";
            }

            start += marker.Length;
            int end = sceneText.IndexOf('\n', start);
            if (end < 0)
            {
                end = sceneText.Length;
            }

            return sceneText[start..end].Trim();
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.InputBackend)
                as TestInputBackend
                ?? throw new InvalidOperationException("Playground test input backend is missing.");
        }

        private static List<string> ExtractOverlayText(ScreenOverlayBuffer overlay)
        {
            var lines = new List<string>();
            foreach (var item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            return lines;
        }

        private static string ExtractUiSceneText(UiScene scene)
        {
            if (scene.Root == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            AppendUiNodeText(scene.Root, sb);
            return sb.ToString();
        }

        private static void AppendUiNodeText(UiNode node, StringBuilder sb)
        {
            if (!string.IsNullOrWhiteSpace(node.TextContent))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(node.TextContent);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                AppendUiNodeText(node.Children[i], sb);
            }
        }

        private static string BuildTraceJsonl(IReadOnlyList<PlayableSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"nav2d-playable-{i + 1:000}",
                    step = snapshot.Step,
                    scenario_name = snapshot.ScenarioName,
                    active_mode_id = snapshot.ActiveModeId,
                    active_camera_id = snapshot.ActiveCameraId,
                    tool_mode = snapshot.ToolMode,
                    agents_per_team = snapshot.AgentsPerTeam,
                    live_agents = snapshot.LiveAgents,
                    blockers = snapshot.Blockers,
                    spawn_batch = snapshot.SpawnBatch,
                    selected_count = snapshot.SelectedCount,
                    selected_ids = snapshot.SelectedIds,
                    primary_goal_x = snapshot.PrimaryGoalX,
                    primary_goal_y = snapshot.PrimaryGoalY,
                    is_following = snapshot.IsFollowing,
                    panel_scene_version = snapshot.PanelSceneVersion,
                    scene_text_head = snapshot.SceneTextHead,
                    overlay_head = snapshot.OverlayHead,
                    tick_ms = Math.Round(snapshot.TickMs, 4),
                    flow_enabled = snapshot.FlowEnabled,
                    flow_debug_enabled = snapshot.FlowDebugEnabled,
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(IReadOnlyList<PlayableSnapshot> snapshots, IReadOnlyList<double> frameTimesMs)
        {
            var timeline = new StringBuilder();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                timeline.AppendLine(
                    $"- [T+{i + 1:000}] {snapshot.Step} | Mode={snapshot.ActiveModeId} | Camera={snapshot.ActiveCameraId} | Tool={snapshot.ToolMode} | Selected={snapshot.SelectedCount} | Live={snapshot.LiveAgents} | Blockers={snapshot.Blockers} | Goal=({snapshot.PrimaryGoalX?.ToString("F0") ?? "n/a"},{snapshot.PrimaryGoalY?.ToString("F0") ?? "n/a"}) | Tick={snapshot.TickMs:F3}ms");
            }

            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var finalSnapshot = snapshots[^1];

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-playground-playable");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: open the real Navigation2D playground, box-select a crowd slice, right-click move it, spawn more units and blockers, and swap between RTS and follow cameras.");
            sb.AppendLine("- Gameplay domain: `Navigation2DPlaygroundMod` through `CoreInputMod`, `UIRoot` + `ReactivePage`, virtual camera view modes, and real Navigation2D simulation.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`");
            sb.AppendLine("- Mods: `LudotsCoreMod`, `CoreInputMod`, `Navigation2DPlaygroundMod`");
            sb.AppendLine($"- Scenario baseline: `Pass Through`, `{AcceptanceAgentsPerTeam}` agents per team, spawn batch `{AcceptanceSpawnBatch}`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
            sb.AppendLine("- Input source: `PlayerInputHandler` backed by a deterministic mouse/keyboard test backend.");
            sb.AppendLine("- Screen mapping: deterministic world/screen transform used by both `IScreenProjector` and `IScreenRayProvider`.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Boot the real engine with `CoreInputMod` and mount the playground panel into `UIRoot`.");
            sb.AppendLine("2. Drag-select the full team-0 spawn block through the shared CoreInput selection system.");
            sb.AppendLine("3. Right-click a new ground point in move mode and verify all selected `NavGoal2D` targets move with formation offsets.");
            sb.AppendLine("4. Switch tools with keyboard hotkeys, then right-click spawn team-0 agents and blockers.");
            sb.AppendLine("5. Press `F2` to enter follow view mode, then `F1` back to RTS command mode.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: panel, selection, command, spawn, and camera mode switching all execute through the real runtime pipeline.");
            sb.AppendLine("- Failure branch condition: UI remounts instead of staying reactive, selection does not fill the shared `SelectionBuffer`, right-click does not update `NavGoal2D`, or spawn counts do not change.");
            sb.AppendLine("- Key metrics: selected entity count, live agents, blocker count, active mode id, active camera id, primary selected goal, headless tick cost.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-playable/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-playable/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-playable/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine($"- verdict: the playground now exposes a formal playable path with mounted reactive UI, CoreInput box selection, right-click move/spawn tools, and view-mode driven camera switching.");
            sb.AppendLine($"- reason: final state returned to `{finalSnapshot.ActiveModeId}` with `{finalSnapshot.SelectedCount}` selected entities preserved, `{finalSnapshot.LiveAgents}` live agents, `{finalSnapshot.Blockers}` blockers, and median tick `{medianTickMs:F3}ms`.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine($"- final active camera: `{finalSnapshot.ActiveCameraId}`");
            sb.AppendLine($"- final selected ids sample: `{string.Join(", ", finalSnapshot.SelectedIds.Take(4))}`");
            sb.AppendLine("- reusable wiring: `ConfigPipeline`, `CoreInputMod`, `UIRoot`, `ReactivePage`, `ViewModeManager`, `Navigation2DRuntime`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Boot GameEngine with CoreInputMod + Navigation2DPlaygroundMod] --> B[Mount UIRoot reactive panel]",
                "    B --> C[Drag box over team-0 crowd]",
                "    C --> D[SelectionBuffer + SelectedTag updated]",
                "    D --> E[Right click move target]",
                "    E --> F[Selected NavGoal2D updated in formation]",
                "    F --> G[Hotkey switch to spawn tool]",
                "    G --> H[Right click spawn agents and blockers]",
                "    H --> I[F2 follow mode]",
                "    I --> J[F1 command mode]",
                "    J --> K[Write battle-report + trace + path]"
            }) + Environment.NewLine;
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var sorted = values.OrderBy(v => v).ToArray();
            int mid = sorted.Length / 2;
            return (sorted.Length & 1) != 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) * 0.5d;
        }

        private static void ResetPlaygroundStaticState()
        {
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private readonly record struct ScreenRect(Vector2 Min, Vector2 Max);

        private readonly record struct PlayableSnapshot(
            string Step,
            string ScenarioName,
            string ActiveModeId,
            string ActiveCameraId,
            string ToolMode,
            int AgentsPerTeam,
            int LiveAgents,
            int Blockers,
            int SpawnBatch,
            int SelectedCount,
            string[] SelectedIds,
            float? PrimaryGoalX,
            float? PrimaryGoalY,
            bool IsFollowing,
            long PanelSceneVersion,
            string SceneTextHead,
            string[] OverlayHead,
            double TickMs,
            bool FlowEnabled,
            bool FlowDebugEnabled);

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out var isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class WorldScreenMapping : IScreenProjector, IScreenRayProvider
        {
            private readonly Vector2 _screenCenter;
            private readonly float _pixelsPerCm;

            public WorldScreenMapping(Vector2 screenCenter, float pixelsPerCm)
            {
                _screenCenter = screenCenter;
                _pixelsPerCm = pixelsPerCm;
            }

            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(
                    _screenCenter.X + worldPosition.X * 100f * _pixelsPerCm,
                    _screenCenter.Y + worldPosition.Z * 100f * _pixelsPerCm);
            }

            public ScreenRay GetRay(Vector2 screenPosition)
            {
                float worldX = (screenPosition.X - _screenCenter.X) / _pixelsPerCm;
                float worldY = (screenPosition.Y - _screenCenter.Y) / _pixelsPerCm;
                return new ScreenRay(new Vector3(worldX / 100f, 10f, worldY / 100f), -Vector3.UnitY);
            }
        }
    }
}
