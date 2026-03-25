using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class NarrativeShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string TestInputBackendKey = "Tests.NarrativeShowcase.InputBackend";
        private const string MapId = "narrative_showcase_hub";
        private const string LolModeId = "Interaction.Mode.LoL";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "InteractionShowcaseMod",
            "NarrativeShowcaseMod"
        };

        private static readonly string[] TrackedNames =
        {
            NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName,
            NarrativeShowcaseMod.NarrativeShowcaseIds.ElderName,
            NarrativeShowcaseMod.NarrativeShowcaseIds.ShrineName,
            NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName
        };

        [Test]
        public void NarrativeShowcase_PlayableFlow_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "narrative-showcase");
            string screensDir = Path.Combine(artifactDir, "screens");
            Directory.CreateDirectory(screensDir);

            var snapshots = new List<AcceptanceSnapshot>();
            var timeline = new List<string>();
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot was not installed.");
            var backend = GetInputBackend(engine);
            var director = engine.GetService(CoreServiceKeys.NarrativeDirector)
                ?? throw new InvalidOperationException("NarrativeDirector was not installed.");

            LoadMap(engine, MapId, frameTimesMs, 8);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));
            Assert.That(ExtractUiText(uiRoot).Any(text => text.Contains("Narrative Showcase", StringComparison.Ordinal)), Is.True);
            AssertQuestStage(director, NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, NarrativeQuestState.Active, "briefing");
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "map_loaded");
            timeline.Add("[T+001] Loaded the narrative showcase hub; HUD mounted and quest entered briefing stage.");

            SelectNamedEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.HasActiveDialogue && !director.HasActiveCinematic, 30);
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "intro_complete");
            timeline.Add("[T+002] Advanced the intro cinematic through the shared narrative input path and handed off into elder dialogue.");

            PressButton(engine, backend, "<Keyboard>/1", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.BuildDialogueSummary().Contains("ember-memory", StringComparison.OrdinalIgnoreCase), 20);
            PressButton(engine, backend, "<Keyboard>/1", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.BuildDialogueSummary().Contains("Wake what sleeps beneath it", StringComparison.OrdinalIgnoreCase), 20);
            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => !director.HasActiveDialogue, 20);
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.LoreVariableId).IntValue, Is.EqualTo(1));
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.TrustVariableId).IntValue, Is.EqualTo(2));
            AssertQuestStage(director, NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, NarrativeQuestState.Active, "trial");
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "briefing_branch_complete");
            timeline.Add("[T+003] Took the lore branch, raised shared narrative variables, and advanced the reusable quest runtime into the trial stage.");

            float baselineMoveSpeed = ReadAttribute(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, "MoveSpeed");
            MoveNearEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.ShrineName, 250f, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.HasActiveCinematic, 20);
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "shrine_interacted");
            timeline.Add("[T+004] Drove the ECS move/order loop to the shrine and triggered the reveal cinematic through the showcase interaction system.");

            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => FindEntityByName(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName) != Entity.Null, 60);
            Entity beast = FindEntityByName(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName);
            Assert.That(beast, Is.Not.EqualTo(Entity.Null));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "beast_spawned");
            timeline.Add("[T+005] Completed the reveal cinematic, let the callback emit the spawn signal, and observed the beast arrive through the runtime entity queue.");

            float beastHealthBeforeInput = ReadHealth(engine.World, beast);
            SetMouseWorld(engine, backend, GetEntityScreen(engine, NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName), frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            Tick(engine, 8, frameTimesMs);
            float beastHealthAfterInput = ReadHealth(engine.World, beast);
            Assert.That(beastHealthAfterInput, Is.LessThan(beastHealthBeforeInput));
            timeline.Add($"[T+006] Used Arcweaver's inherited combat input on the spawned beast; HP {beastHealthBeforeInput:0.##} -> {beastHealthAfterInput:0.##}.");

            ApplyDeterministicGasFinisher(engine, FindEntityByName(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName), beast, frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.TryGetQuestState(NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, out var state, out string stageId) && state == NarrativeQuestState.Active && string.Equals(stageId, "return", StringComparison.OrdinalIgnoreCase), 120);
            Assert.That(ReadHealth(engine.World, beast), Is.LessThanOrEqualTo(0f));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "beast_defeated");
            timeline.Add("[T+007] Finished the encounter through GAS effects, which the narrative runtime converted into the return stage via signal tracking.");

            MoveNearEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.ElderName, 260f, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.HasActiveDialogue, 30);
            PressButton(engine, backend, "<Keyboard>/2", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.TryGetQuestState(NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, out var state, out _) && state == NarrativeQuestState.Completed, 60);
            Tick(engine, 10, frameTimesMs);
            float rewardedMoveSpeed = ReadAttribute(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, "MoveSpeed");
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.EndingVariableId).StringValue, Is.EqualTo("Mercy"));
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.TrustVariableId).IntValue, Is.EqualTo(4));
            Assert.That(rewardedMoveSpeed, Is.GreaterThan(baselineMoveSpeed));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frameTimesMs, screensDir, "mercy_ending");
            timeline.Add("[T+008] Returned to the elder, unlocked the Mercy branch from earlier lore knowledge, completed the quest, and received the trigger-driven GAS blessing reward.");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            WriteTimelineSheet(snapshots, screensDir, Path.Combine(screensDir, "timeline.png"));
        }
        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new WorldMappedScreenRayProvider());
            engine.SetService(CoreServiceKeys.ScreenProjector, new WorldMappedScreenProjector());

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);

            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Tick(engine, frames, frameTimesMs);
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
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate()) return;
                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
            => engine.GlobalContext[TestInputBackendKey] as TestInputBackend ?? throw new InvalidOperationException("Missing input backend.");

        private static void SelectNamedEntity(GameEngine engine, TestInputBackend backend, string name, List<double> frameTimesMs)
        {
            LeftClickWorld(engine, backend, GetEntityScreen(engine, name), frameTimesMs);
            TickUntil(engine, frameTimesMs, () => string.Equals(GetSelectedEntityName(engine), name, StringComparison.Ordinal) && GetSelectionCount(engine) == 1, 20);
        }

        private static void MoveNearEntity(GameEngine engine, TestInputBackend backend, string targetName, float withinCm, List<double> frameTimesMs)
        {
            RightClickWorld(engine, backend, GetEntityScreen(engine, targetName), frameTimesMs);
            TickUntil(engine, frameTimesMs, () => Vector2.Distance(ReadPosition(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName), ReadPosition(engine.World, targetName)) <= withinCm, 240);
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void LeftClickWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            SetMouseWorld(engine, backend, screenPosition, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void RightClickWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            SetMouseWorld(engine, backend, screenPosition, frameTimesMs);
            backend.SetButton("<Mouse>/RightButton", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Mouse>/RightButton", false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void SetMouseWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            backend.SetMousePosition(screenPosition);
            Tick(engine, 1, frameTimesMs);
        }

        private static void ApplyDeterministicGasFinisher(GameEngine engine, Entity source, Entity target, List<double> frameTimesMs)
        {
            var queue = engine.GetService(CoreServiceKeys.EffectRequestQueue) ?? throw new InvalidOperationException("Missing EffectRequestQueue.");
            int templateId = EffectTemplateIdRegistry.GetId("Effect.Interaction.DuelBolt");
            Assert.That(templateId, Is.GreaterThan(0));
            for (int i = 0; i < 16; i++)
            {
                if (ReadHealth(engine.World, target) <= 0f) return;
                queue.Publish(new EffectRequest { Source = source, Target = target, TemplateId = templateId });
                Tick(engine, 4, frameTimesMs);
            }

            Assert.That(ReadHealth(engine.World, target), Is.LessThanOrEqualTo(0f));
        }

        private static void AssertQuestStage(NarrativeDirector director, string questId, NarrativeQuestState expectedState, string expectedStage)
        {
            Assert.That(director.TryGetQuestState(questId, out var actualState, out string actualStage), Is.True);
            Assert.That(actualState, Is.EqualTo(expectedState));
            Assert.That(actualStage, Is.EqualTo(expectedStage));
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (result == Entity.Null && string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static Vector2 ReadPosition(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null));
            return ReadPosition(world, entity);
        }

        private static Vector2 ReadPosition(World world, Entity entity)
        {
            Assert.That(world.TryGet(entity, out WorldPositionCm position), Is.True);
            var worldCm = position.ToWorldCmInt2();
            return new Vector2(worldCm.X, worldCm.Y);
        }

        private static float ReadHealth(World world, Entity entity)
        {
            int healthId = AttributeRegistry.GetId("Health");
            return healthId < 0 || !world.TryGet(entity, out AttributeBuffer attributes) ? 0f : attributes.GetCurrent(healthId);
        }

        private static float ReadAttribute(World world, string name, string attributeName)
        {
            Entity entity = FindEntityByName(world, name);
            int attributeId = AttributeRegistry.GetId(attributeName);
            Assert.That(attributeId, Is.GreaterThan(0));
            Assert.That(world.TryGet(entity, out AttributeBuffer attributes), Is.True);
            return attributes.GetCurrent(attributeId);
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector) ?? throw new InvalidOperationException("Missing ScreenProjector.");
            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            return projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static int GetSelectionCount(GameEngine engine)
            => SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext).Length;

        private static string GetSelectedEntityName(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) && engine.World.TryGet(selected, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static string GetActiveModeId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreInputMod.ViewMode.ViewModeManager.ActiveModeIdKey, out var modeIdObj) && modeIdObj is string modeId
                ? modeId
                : string.Empty;
        }
        private static List<string> ExtractUiText(UIRoot root)
        {
            if (root.Scene?.Root == null) return new List<string>();
            var lines = new List<string>();
            CollectUiText(root.Scene.Root, lines);
            return lines;
        }

        private static void CollectUiText(UiNode node, List<string> lines)
        {
            if (!string.IsNullOrWhiteSpace(node.TextContent)) lines.Add(node.TextContent.Trim());
            for (int i = 0; i < node.Children.Count; i++) CollectUiText(node.Children[i], lines);
        }

        private static void CaptureSnapshot(GameEngine engine, UIRoot uiRoot, NarrativeDirector director, List<AcceptanceSnapshot> snapshots, IReadOnlyList<double> frameTimesMs, string screensDir, string step)
        {
            var entities = new List<EntityState>(TrackedNames.Length);
            for (int i = 0; i < TrackedNames.Length; i++) entities.Add(BuildEntityState(engine.World, TrackedNames[i]));

            var snapshot = new AcceptanceSnapshot(
                step,
                director.BuildQuestSummary(),
                director.BuildObjectiveSummary(),
                director.BuildDialogueSummary(),
                director.BuildCinematicSummary(),
                director.BuildVariableSummary(NarrativeShowcaseMod.NarrativeShowcaseIds.TrustVariableId, NarrativeShowcaseMod.NarrativeShowcaseIds.LoreVariableId, NarrativeShowcaseMod.NarrativeShowcaseIds.EndingVariableId),
                ExtractUiText(uiRoot).Take(10).ToArray(),
                GetActiveModeId(engine),
                frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d,
                entities);
            snapshots.Add(snapshot);
            WriteSnapshotImage(snapshot, Path.Combine(screensDir, $"{snapshots.Count:000}_{step}.png"));
        }

        private static EntityState BuildEntityState(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            if (entity == Entity.Null || !world.IsAlive(entity)) return new EntityState(name, false, 0f, 0f, 0f);
            Vector2 pos = ReadPosition(world, entity);
            return new EntityState(name, true, pos.X, pos.Y, ReadHealth(world, entity));
        }

        private static void WriteSnapshotImage(AcceptanceSnapshot snapshot, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing screenshot dir."));
            using var surface = SKSurface.Create(new SKImageInfo(ImageWidth, ImageHeight));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(11, 18, 28));

            using var panelPaint = new SKPaint { Color = new SKColor(18, 32, 48), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var panelStroke = new SKPaint { Color = new SKColor(82, 147, 214), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
            using var playerPaint = new SKPaint { Color = new SKColor(102, 231, 146), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var elderPaint = new SKPaint { Color = new SKColor(244, 198, 94), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var shrinePaint = new SKPaint { Color = new SKColor(111, 193, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var beastPaint = new SKPaint { Color = new SKColor(255, 102, 102), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 26f };
            using var subTextPaint = new SKPaint { Color = new SKColor(201, 213, 227), IsAntialias = true, TextSize = 20f };
            using var faintTextPaint = new SKPaint { Color = new SKColor(141, 160, 184), IsAntialias = true, TextSize = 18f };

            SKRect mapRect = new SKRect(40, 120, 820, 860);
            SKRect infoRect = new SKRect(860, 120, 1560, 860);
            canvas.DrawRoundRect(mapRect, 18f, 18f, panelPaint);
            canvas.DrawRoundRect(mapRect, 18f, 18f, panelStroke);
            canvas.DrawRoundRect(infoRect, 18f, 18f, panelPaint);
            canvas.DrawRoundRect(infoRect, 18f, 18f, panelStroke);
            canvas.DrawText($"Narrative Showcase | {snapshot.Step}", 40, 54, textPaint);
            canvas.DrawText($"Mode={snapshot.ActiveModeId}  Tick={snapshot.TickMs:F3}ms", 40, 84, faintTextPaint);
            canvas.DrawText("Showcase Space", mapRect.Left + 20, mapRect.Top + 32, subTextPaint);
            canvas.DrawText("Narrative HUD", infoRect.Left + 20, infoRect.Top + 32, subTextPaint);

            var alive = snapshot.Entities.Where(entity => entity.Alive).ToArray();
            float minX = alive.Length == 0 ? 0f : alive.Min(entity => entity.X) - 180f;
            float maxX = alive.Length == 0 ? 2200f : alive.Max(entity => entity.X) + 180f;
            float minY = alive.Length == 0 ? 0f : alive.Min(entity => entity.Y) - 180f;
            float maxY = alive.Length == 0 ? 2200f : alive.Max(entity => entity.Y) + 180f;
            float width = MathF.Max(1f, maxX - minX);
            float height = MathF.Max(1f, maxY - minY);

            foreach (var entity in snapshot.Entities)
            {
                if (!entity.Alive) continue;
                float px = mapRect.Left + 40f + ((entity.X - minX) / width) * (mapRect.Width - 80f);
                float py = mapRect.Bottom - 40f - ((entity.Y - minY) / height) * (mapRect.Height - 80f);
                SKPaint paint = entity.Name switch
                {
                    var name when string.Equals(name, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, StringComparison.OrdinalIgnoreCase) => playerPaint,
                    var name when string.Equals(name, NarrativeShowcaseMod.NarrativeShowcaseIds.ElderName, StringComparison.OrdinalIgnoreCase) => elderPaint,
                    var name when string.Equals(name, NarrativeShowcaseMod.NarrativeShowcaseIds.ShrineName, StringComparison.OrdinalIgnoreCase) => shrinePaint,
                    _ => beastPaint
                };
                canvas.DrawCircle(px, py, 12f, paint);
                canvas.DrawText($"{entity.Name} ({entity.Health:0.#})", px + 18f, py + 6f, faintTextPaint);
            }

            float y = infoRect.Top + 72f;
            DrawInfoLine(canvas, textPaint, infoRect.Left + 20f, ref y, snapshot.QuestSummary);
            DrawInfoLine(canvas, subTextPaint, infoRect.Left + 20f, ref y, snapshot.ObjectiveSummary);
            DrawInfoLine(canvas, subTextPaint, infoRect.Left + 20f, ref y, snapshot.DialogueSummary);
            DrawInfoLine(canvas, subTextPaint, infoRect.Left + 20f, ref y, snapshot.CinematicSummary);
            DrawInfoLine(canvas, subTextPaint, infoRect.Left + 20f, ref y, snapshot.VariableSummary);
            DrawInfoLine(canvas, faintTextPaint, infoRect.Left + 20f, ref y, "UI excerpt:");
            for (int i = 0; i < snapshot.UiText.Count && i < 5; i++) DrawInfoLine(canvas, faintTextPaint, infoRect.Left + 20f, ref y, $"- {snapshot.UiText[i]}");

            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
        }

        private static void DrawInfoLine(SKCanvas canvas, SKPaint paint, float x, ref float y, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            canvas.DrawText(text, x, y, paint);
            y += paint.TextSize + 10f;
        }

        private static void WriteTimelineSheet(IReadOnlyList<AcceptanceSnapshot> snapshots, string screensDir, string outputPath)
        {
            if (snapshots.Count == 0) return;
            const int thumbWidth = 760;
            const int thumbHeight = 427;
            const int columns = 2;
            int rows = (int)Math.Ceiling(snapshots.Count / (double)columns);
            using var surface = SKSurface.Create(new SKImageInfo(columns * thumbWidth, rows * thumbHeight + 60));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(8, 10, 16));
            using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
            canvas.DrawText("Narrative showcase screenshot timeline", 20, 36, titlePaint);
            for (int i = 0; i < snapshots.Count; i++)
            {
                string sourcePath = Path.Combine(screensDir, $"{i + 1:000}_{snapshots[i].Step}.png");
                if (!File.Exists(sourcePath)) continue;
                using SKBitmap bitmap = SKBitmap.Decode(sourcePath);
                int col = i % columns;
                int row = i / columns;
                SKRect dest = new SKRect(col * thumbWidth, row * thumbHeight + 60, (col + 1) * thumbWidth, (row + 1) * thumbHeight + 60);
                canvas.DrawBitmap(bitmap, dest);
            }
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
        }
        private static string BuildTraceJsonl(IReadOnlyList<AcceptanceSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"narrative-showcase-{i + 1:000}",
                    step = snapshot.Step,
                    quest = snapshot.QuestSummary,
                    objective = snapshot.ObjectiveSummary,
                    dialogue = snapshot.DialogueSummary,
                    cinematic = snapshot.CinematicSummary,
                    variables = snapshot.VariableSummary,
                    active_mode_id = snapshot.ActiveModeId,
                    tick_ms = Math.Round(snapshot.TickMs, 4),
                    entities = snapshot.Entities,
                    ui_head = snapshot.UiText.Take(4).ToArray(),
                    status = "done"
                }));
            }
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<AcceptanceSnapshot> snapshots, IReadOnlyList<double> frameTimesMs)
        {
            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var final = snapshots[^1];
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: narrative-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: play a full quest/dialogue/cinematic loop that starts in a camera-led intro, branches on dialogue knowledge, wakes a shrine, defeats a spawned beast, and returns for an ending choice.");
            sb.AppendLine("- Gameplay domain: shared Ludots ECS movement, interaction showcase combat/GAS, trigger callbacks, runtime entity spawning, virtual cameras, and reactive UI state.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Map: `narrative_showcase_hub`");
            sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
            sb.AppendLine("- Input source: real `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic backend injections.");
            sb.AppendLine("- Narrative branches exercised: briefing lore path -> return mercy path.");
            sb.AppendLine("- Evidence PNG panels: `screens/001_map_loaded.png`, `screens/002_intro_complete.png`, `screens/003_briefing_branch_complete.png`, `screens/004_shrine_interacted.png`, `screens/005_beast_spawned.png`, `screens/006_beast_defeated.png`, `screens/007_mercy_ending.png`, `screens/timeline.png`");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Boot the real engine with the narrative showcase mod and load the hub map.");
            sb.AppendLine("2. Advance the intro cinematic, then choose the lore branch and accept the trial in elder dialogue.");
            sb.AppendLine("3. Move to the shrine through the production order path and trigger the reveal callback.");
            sb.AppendLine("4. Damage the spawned beast through the inherited interaction combat input, then finish it through deterministic GAS effect application.");
            sb.AppendLine("5. Return to the elder, choose the Mercy ending, and validate the trigger-driven GAS blessing reward.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++) sb.AppendLine($"- {timeline[i]}");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine($"- final quest: `{final.QuestSummary}`");
            sb.AppendLine($"- final variables: `{final.VariableSummary}`");
            sb.AppendLine($"- final dialogue card: `{final.DialogueSummary}`");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine("- reused infrastructure: `ConfigPipeline`, `NarrativeDirector`, `TriggerManager`, `RuntimeEntitySpawnQueue`, `EffectRequestQueue`, `VirtualCameraRegistry`, `PlayerInputHandler`, `SelectionContextRuntime`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load narrative_showcase_hub] --> B[Intro cinematic steps advance]",
                "    B --> C[Briefing dialogue lore branch]",
                "    C --> D[Quest stage advances to trial]",
                "    D --> E[Right-click move near shrine]",
                "    E --> F[Press E -> reveal cinematic]",
                "    F --> G[Trigger callback spawns Ashen Beast]",
                "    G --> H[Arcweaver combat damages beast]",
                "    H --> I[GAS finisher defeats beast -> signal emitted]",
                "    I --> J[Quest stage advances to return]",
                "    J --> K[Return dialogue unlocks Mercy branch]",
                "    K --> L[Reward signal applies GAS blessing and quest completes]"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "assets"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return 0d;
            var ordered = values.OrderBy(v => v).ToArray();
            int middle = ordered.Length / 2;
            return (ordered.Length & 1) == 0 ? (ordered[middle - 1] + ordered[middle]) * 0.5d : ordered[middle];
        }

        private sealed record AcceptanceSnapshot(string Step, string QuestSummary, string ObjectiveSummary, string DialogueSummary, string CinematicSummary, string VariableSummary, IReadOnlyList<string> UiText, string ActiveModeId, double TickMs, IReadOnlyList<EntityState> Entities);
        private sealed record EntityState(string Name, bool Alive, float X, float Y, float Health);

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;
            public void SetButton(string path, bool isDown) => _buttons[path] = isDown;
            public void SetMousePosition(Vector2 position) => _mousePosition = position;
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
            public StubViewController(float width, float height) => Resolution = new Vector2(width, height);
            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition) => new(new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f), -Vector3.UnitY);
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition) => new(worldPosition.X * 100f, worldPosition.Z * 100f);
        }
    }
}



