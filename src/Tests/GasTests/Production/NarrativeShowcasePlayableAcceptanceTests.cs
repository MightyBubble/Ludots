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
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Quests;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class NarrativeShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string TestInputBackendKey = "Tests.NarrativeShowcase.InputBackend";
        private const string HeadlessCameraKey = "Tests.NarrativeShowcase.HeadlessCamera";
        private const string MapId = "narrative_showcase_hub";
        private const string LolModeId = "Interaction.Mode.LoL";
        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "NarrativeFrontendMod",
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
            AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

            var snapshots = new List<AcceptanceSnapshot>();
            var frames = new List<UiAcceptanceEvidenceFrame>();
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
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains("Narrative Showcase", StringComparison.Ordinal)), Is.True);
            AssertQuestStage(director, NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, QuestState.Active, "briefing");
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "map_loaded");
            timeline.Add("[T+001] Loaded the narrative showcase hub; HUD mounted and quest entered briefing stage.");

            SelectNamedEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.HasActiveDialogue && !director.HasActiveCinematic, 30);
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "intro_complete");
            timeline.Add("[T+002] Advanced the intro cinematic through the shared narrative input path and handed off into elder dialogue.");

            PressButton(engine, backend, "<Keyboard>/1", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.BuildDialogueSummary().Contains("ember-memory", StringComparison.OrdinalIgnoreCase), 20);
            PressButton(engine, backend, "<Keyboard>/1", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.BuildDialogueSummary().Contains("Wake what sleeps beneath it", StringComparison.OrdinalIgnoreCase), 20);
            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => !director.HasActiveDialogue, 20);
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.LoreVariableId).IntValue, Is.EqualTo(1));
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.TrustVariableId).IntValue, Is.EqualTo(2));
            AssertQuestStage(director, NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, QuestState.Active, "trial");
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "briefing_branch_complete");
            timeline.Add("[T+003] Took the lore branch, raised shared narrative variables, and advanced the reusable quest runtime into the trial stage.");

            float baselineMoveSpeed = ReadAttribute(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, "MoveSpeed");
            MoveNearEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.ShrineName, 250f, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => director.HasActiveCinematic && UiContains(uiRoot, "Auto Bubble"),
                40);
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "shrine_interacted");
            timeline.Add("[T+004] Drove the ECS move/order loop to the shrine and triggered the reveal cinematic through the showcase interaction system.");

            PressButton(engine, backend, "<Keyboard>/enter", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => FindEntityByName(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName) != Entity.Null, 60);
            Entity beast = FindEntityByName(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName);
            Assert.That(beast, Is.Not.EqualTo(Entity.Null));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "beast_spawned");
            timeline.Add("[T+005] Completed the reveal cinematic, let the callback emit the spawn signal, and observed the beast arrive through the runtime entity queue.");
            WaitForCameraBlendToComplete(engine, frameTimesMs);

            float beastHealthBeforeInput = ReadHealth(engine.World, beast);
            AimAtNamedEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.BeastName, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            Tick(engine, 8, frameTimesMs);
            float beastHealthAfterInput = ReadHealth(engine.World, beast);
            Assert.That(beastHealthAfterInput, Is.LessThan(beastHealthBeforeInput), BuildCombatInputDiagnostics(engine, beast));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "beast_pressured");
            timeline.Add($"[T+006] Used Arcweaver's inherited combat input on the spawned beast; HP {beastHealthBeforeInput:0.##} -> {beastHealthAfterInput:0.##}.");

            ApplyDeterministicGasFinisher(engine, FindEntityByName(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName), beast, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => director.TryGetQuestState(NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, out var state, out string stageId) && state == QuestState.Active && string.Equals(stageId, "return", StringComparison.OrdinalIgnoreCase),
                120,
                () => BuildQuestProgressDiagnostics(engine, director, beast));
            Assert.That(ReadHealth(engine.World, beast), Is.LessThanOrEqualTo(0f));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "beast_defeated");
            timeline.Add("[T+007] Finished the encounter through GAS effects, which the narrative runtime converted into the return stage via signal tracking.");

            MoveNearEntity(engine, backend, NarrativeShowcaseMod.NarrativeShowcaseIds.ElderName, 260f, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.HasActiveDialogue, 30);
            PressButton(engine, backend, "<Keyboard>/2", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => director.TryGetQuestState(NarrativeShowcaseMod.NarrativeShowcaseIds.QuestId, out var state, out _) && state == QuestState.Completed, 60);
            Tick(engine, 10, frameTimesMs);
            float rewardedMoveSpeed = ReadAttribute(engine.World, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName, "MoveSpeed");
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.EndingVariableId).StringValue, Is.EqualTo("Mercy"));
            Assert.That(director.GetVariable(NarrativeShowcaseMod.NarrativeShowcaseIds.TrustVariableId).IntValue, Is.EqualTo(4));
            Assert.That(rewardedMoveSpeed, Is.GreaterThan(baselineMoveSpeed));
            CaptureSnapshot(engine, uiRoot, director, snapshots, frames, frameTimesMs, screensDir, "mercy_ending");
            timeline.Add("[T+008] Returned to the elder, unlocked the Mercy branch from earlier lore knowledge, completed the quest, and received the trigger-driven GAS blessing reward.");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frames, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            AcceptanceUiEvidenceWriter.WriteTimelineSheet(frames, screensDir, Path.Combine(screensDir, "timeline.png"), "Narrative showcase 5W1H screenshot flow");
            AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("narrative-showcase", frames, Path.Combine(artifactDir, "5w1h.md"));
        }
        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            AcceptanceUiHostInstaller.Install(engine);

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            if (engine.GlobalContext[TestInputBackendKey] is TestInputBackend backend)
            {
                backend.SetMousePosition(view.Resolution * 0.5f);
            }

            var cameraAdapter = new StubCameraAdapter();
            var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.AuthorityCamera(), view);
            var screenRayProvider = new CoreScreenRayProvider(engine.AuthorityCamera(), view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(engine.World, engine.AuthorityCamera(), engine.SpatialQueries, view, cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext[HeadlessCameraKey] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup));

            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Tick(engine, frames, frameTimesMs);
            WaitForCameraBlendToComplete(engine, frameTimesMs);
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
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                UpdateHeadlessCamera(engine);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> predicate,
            int maxFrames,
            Func<string>? describeFailure = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate()) return;
                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames. {describeFailure?.Invoke()}");
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
            => engine.GlobalContext[TestInputBackendKey] as TestInputBackend ?? throw new InvalidOperationException("Missing input backend.");

        private static void SelectNamedEntity(GameEngine engine, TestInputBackend backend, string name, List<double> frameTimesMs)
        {
            Vector2 screenPoint = GetEntityScreen(engine, name);
            LeftClickWorld(engine, backend, screenPoint, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => string.Equals(GetSelectedEntityName(engine), name, StringComparison.Ordinal) && GetSelectionCount(engine) == 1,
                20,
                () => BuildClickSelectionDiagnostics(engine, name, screenPoint));
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

        private static void AimAtNamedEntity(GameEngine engine, TestInputBackend backend, string name, List<double> frameTimesMs)
        {
            Vector2 screenPoint = Vector2.Zero;
            for (int i = 0; i < 12; i++)
            {
                screenPoint = GetEntityScreen(engine, name);
                SetMouseWorld(engine, backend, screenPoint, frameTimesMs);
                if (string.Equals(GetHoveredEntityName(engine), name, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.That(GetHoveredEntityName(engine), Is.EqualTo(name), BuildClickSelectionDiagnostics(engine, name, screenPoint));
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

        private static void AssertQuestStage(NarrativeDirector director, string questId, QuestState expectedState, string expectedStage)
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
            Assert.That(attributeId, Is.Not.EqualTo(AttributeRegistry.InvalidId));
            Assert.That(world.TryGet(entity, out AttributeBuffer attributes), Is.True);
            return attributes.GetCurrent(attributeId);
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string name)
        {
            UpdateHeadlessCamera(engine);
            Entity entity = FindEntityByName(engine.World, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector) ?? throw new InvalidOperationException("Missing ScreenProjector.");
            if (SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect bounds))
            {
                return new Vector2(
                    (bounds.MinX + bounds.MaxX) * 0.5f,
                    (bounds.MinY + bounds.MaxY) * 0.5f);
            }

            if (engine.World.TryGet(entity, out VisualTransform transform))
            {
                return projector.WorldToScreen(transform.Position);
            }

            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            return projector.WorldToScreen(WorldUnitsFix64.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static void WaitForCameraBlendToComplete(GameEngine engine, List<double> frameTimesMs)
        {
            TickUntil(
                engine,
                frameTimesMs,
                () => engine.AuthorityCamera().VirtualCameraBrain?.IsBlending != true,
                60);

            Tick(engine, 1, frameTimesMs);
        }

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(HeadlessCameraKey, out object? runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            runtime.CameraPresenter.Update(engine.AuthorityCamera(), interpolationAlpha: 1f);
        }

        private static int GetSelectionCount(GameEngine engine)
            => Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine).Length;

        private static bool UiContains(UIRoot root, string text)
        {
            return AcceptanceUiEvidenceWriter.ExtractUiText(root)
                .Any(line => line.Contains(text, StringComparison.Ordinal));
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            return Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity selected) && engine.World.TryGet(selected, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static string GetHoveredEntityName(GameEngine engine)
        {
            return TryGetHoveredEntity(engine, out Entity hovered) && engine.World.TryGet(hovered, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static bool TryGetHoveredEntity(GameEngine engine, out Entity hovered)
        {
            hovered = Entity.Null;
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var local) ||
                !engine.World.IsAlive(local))
            {
                return false;
            }

            return engine.GetService(CoreServiceKeys.EntityCollectionStore) is { } collections &&
                   EntityCollectionContextRuntime.TryGetHovered(engine.World, collections, local, out hovered);
        }

        private static string BuildSelectionStateDiagnostics(GameEngine engine)
        {
            var details = new List<string>
            {
                $"selectionCount={GetSelectionCount(engine)}",
                $"primary={GetSelectedEntityName(engine)}"
            };

            if (TryGetHoveredEntity(engine, out Entity hovered) &&
                engine.World.IsAlive(hovered) &&
                engine.World.TryGet(hovered, out Name hoveredName))
            {
                details.Add($"hovered={hoveredName.Value}");
            }
            else
            {
                details.Add("hovered=<none>");
            }

            if (ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity localPlayer) &&
                engine.World.IsAlive(localPlayer) &&
                engine.World.TryGet(localPlayer, out Name localName))
            {
                details.Add($"local={localName.Value}");
                if (engine.World.Has<CommandSourceDragState>(localPlayer))
                {
                    ref var drag = ref engine.World.Get<CommandSourceDragState>(localPlayer);
                    details.Add($"dragActive={drag.Active}");
                    details.Add($"dragStart=({drag.StartScreen.X:0.##},{drag.StartScreen.Y:0.##})");
                    details.Add($"dragCurrent=({drag.CurrentScreen.X:0.##},{drag.CurrentScreen.Y:0.##})");
                }
            }
            else
            {
                details.Add("local=<none>");
            }

            return string.Join(" | ", details);
        }

        private static string BuildCombatInputDiagnostics(GameEngine engine, Entity beast)
        {
            var details = new List<string>
            {
                BuildInputActionDiagnostics(engine, "SkillQ"),
                BuildAbilityDiagnostics(engine, NarrativeShowcaseMod.NarrativeShowcaseIds.PlayerName),
                BuildSelectionStateDiagnostics(engine)
            };

            if (engine.GlobalContext.TryGetValue(CoreInputMod.Systems.LocalOrderSourceHelper.LastGroundWorldDebugKey, out object? ground))
            {
                details.Add($"lastGround={ground}");
            }

            if (engine.GlobalContext.TryGetValue(CoreInputMod.Systems.LocalOrderSourceHelper.LastOrderDebugKey, out object? order))
            {
                details.Add($"lastOrder={order}");
            }

            if (engine.World.IsAlive(beast))
            {
                details.Add($"beastCommandSourceSelectable={engine.World.Has<CommandSourceSelectableTag>(beast)}");
                if (engine.World.TryGet(beast, out WorldPositionCm beastPos))
                {
                    details.Add($"beastPos=({beastPos.Value.X.ToFloat():0.##},{beastPos.Value.Y.ToFloat():0.##})");
                }

                Vector2 pointer = engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input
                    ? input.ReadAction<Vector2>("PointerPos")
                    : Vector2.Zero;
                details.Add(BuildEntityPointerDiagnostics(engine, beast, pointer));
            }

            return string.Join(" || ", details);
        }

        private static string BuildQuestProgressDiagnostics(GameEngine engine, NarrativeDirector director, Entity beast)
        {
            var details = new List<string>
            {
                $"quest={director.BuildQuestSummary()}",
                $"objective={director.BuildObjectiveSummary()}",
                $"beastHealth={ReadHealth(engine.World, beast):0.##}"
            };

            if (director.TryResolveEntity(NarrativeShowcaseMod.NarrativeShowcaseIds.BeastAlias, out Entity boundBeast))
            {
                details.Add($"boundBeast={boundBeast.Id}:{boundBeast.WorldId}:{boundBeast.Version}");
                details.Add($"boundHealth={ReadHealth(engine.World, boundBeast):0.##}");
            }
            else
            {
                details.Add("boundBeast=<none>");
            }

            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseMod.NarrativeShowcaseIds.BeastDefeatedKey, out object? defeated))
            {
                details.Add($"beastDefeatedFlag={defeated}");
            }

            if (engine.GetService(CoreServiceKeys.QuestRuntimeService) is QuestRuntimeService quests)
            {
                quests.Signals.TryGetValue(NarrativeShowcaseMod.NarrativeShowcaseIds.BeastDefeatedSignal, out int signalCount);
                details.Add($"beastSignalCount={signalCount}");
            }

            details.Add($"triggerErrors={engine.TriggerManager.Errors.Count}");
            return string.Join(" | ", details);
        }

        private static string BuildEntityPointerDiagnostics(GameEngine engine, Entity entity, Vector2 pointer)
        {
            var details = new List<string>
            {
                $"pointer=({pointer.X:0.##},{pointer.Y:0.##})"
            };

            if (engine.World.TryGet(entity, out CullState cull))
            {
                details.Add($"cullVisible={cull.IsVisible}");
                details.Add($"lod={cull.LOD}");
                details.Add($"coverage={cull.ScreenCoverage01:0.###}");
            }
            else
            {
                details.Add("cull=<missing>");
            }

            if (engine.World.TryGet(entity, out VisualTransform transform))
            {
                details.Add($"visual=({transform.Position.X:0.##},{transform.Position.Y:0.##},{transform.Position.Z:0.##})");
            }
            else
            {
                details.Add("visual=<missing>");
            }

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            bool hasBounds = SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect bounds);
            details.Add(hasBounds
                ? $"bounds=({bounds.MinX:0.##},{bounds.MinY:0.##})-({bounds.MaxX:0.##},{bounds.MaxY:0.##})"
                : "bounds=<none>");
            details.Add($"pointerHits={SpatialBoundsUtility.PointerHitsEntity(engine.World, entity, projector, pointer, 36f)}");

            return string.Join(" | ", details);
        }

        private static string BuildAbilityDiagnostics(GameEngine engine, string actorName)
        {
            Entity actor = FindEntityByName(engine.World, actorName);
            if (actor == Entity.Null || !engine.World.IsAlive(actor))
            {
                return $"Actor '{actorName}' was not found.";
            }

            var details = new List<string>();
            if (engine.World.TryGet(actor, out OrderBuffer orders))
            {
                details.Add(orders.HasActive ? $"activeOrder={orders.ActiveOrder.Order.OrderTypeId}" : "activeOrder=<none>");
                details.Add(orders.HasPending ? $"pendingOrder={orders.PendingOrder.Order.OrderTypeId}" : "pendingOrder=<none>");
                if (orders.HasQueued)
                {
                    details.Add($"queuedOrders={orders.QueuedCount}");
                }
            }
            else
            {
                details.Add("orderBuffer=<missing>");
            }

            if (engine.World.TryGet(actor, out BlackboardIntBuffer ints) &&
                ints.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex))
            {
                details.Add($"castSlot={slotIndex}");
            }

            if (engine.World.TryGet(actor, out BlackboardSpatialBuffer spatial))
            {
                int pointCount = spatial.GetPointCount(OrderBlackboardKeys.Cast_TargetPosition);
                details.Add($"castPoints={pointCount}");
                if (pointCount > 0 && spatial.TryGetPointAt(OrderBlackboardKeys.Cast_TargetPosition, pointCount - 1, out var point))
                {
                    details.Add($"castTarget=({point.X:0.##},{point.Z:0.##})");
                }
            }

            if (engine.World.TryGet(actor, out AbilityExecInstance exec))
            {
                details.Add($"execSlot={exec.AbilitySlot}");
                details.Add($"execState={exec.State}");
                details.Add($"execHasTargetPos={exec.HasTargetPos != 0}");
                if (exec.HasTargetPos != 0)
                {
                    details.Add($"execTarget=({exec.TargetPosCm.X.ToFloat():0.##},{exec.TargetPosCm.Y.ToFloat():0.##})");
                }
            }
            else
            {
                details.Add("exec=<none>");
            }

            details.Add($"activeMode={GetActiveModeId(engine)}");
            return string.Join(" | ", details);
        }

        private static string BuildInputActionDiagnostics(GameEngine engine, string actionId)
        {
            var details = new List<string>();

            if (engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler liveInput)
            {
                details.Add($"livePressed={liveInput.PressedThisFrame(actionId)}");
                details.Add($"liveDown={liveInput.IsDown(actionId)}");
                Vector2 pointer = liveInput.ReadAction<Vector2>("PointerPos");
                details.Add($"livePointer=({pointer.X:0.##},{pointer.Y:0.##})");
            }
            else
            {
                details.Add("liveInput=<missing>");
            }

            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader authoritativeInput)
            {
                details.Add($"authPressed={authoritativeInput.PressedThisFrame(actionId)}");
                details.Add($"authDown={authoritativeInput.IsDown(actionId)}");
                Vector2 pointer = authoritativeInput.ReadAction<Vector2>("PointerPos");
                details.Add($"authPointer=({pointer.X:0.##},{pointer.Y:0.##})");
            }
            else
            {
                details.Add("authoritativeInput=<missing>");
            }

            if (engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is InputOrderMappingSystem mapping)
            {
                details.Add($"mappingMode={mapping.InteractionMode}");
                details.Add($"mappingAiming={mapping.IsAiming}");
                if (mapping.GetMapping(actionId) is InputOrderMapping actionMapping)
                {
                    details.Add($"targetType={actionMapping.TargetType}");
                    details.Add($"actorCollection={actionMapping.ActorCollectionKey}");
                    details.Add($"targetCollection={actionMapping.TargetCollectionKey}");
                    details.Add($"orderTypeKey={actionMapping.OrderTypeKey}");
                }
                else
                {
                    details.Add("mapping=<missing>");
                }
            }
            else
            {
                details.Add("activeMapping=<missing>");
            }

            bool uiCaptured = engine.GlobalContext.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var uiCapturedObj) &&
                              uiCapturedObj is bool captured &&
                              captured;
            details.Add($"uiCaptured={uiCaptured}");
            details.Add($"activeMode={GetActiveModeId(engine)}");
            return string.Join(" | ", details);
        }

        private static string BuildClickSelectionDiagnostics(GameEngine engine, string entityName, Vector2 screenPoint)
        {
            var details = new List<string>
            {
                $"click=({screenPoint.X:0.##},{screenPoint.Y:0.##})",
                BuildSelectionStateDiagnostics(engine)
            };

            Entity entity = FindEntityByName(engine.World, entityName);
            details.Add($"entityAlive={engine.World.IsAlive(entity)}");
            details.Add($"commandSourceSelectable={engine.World.Has<CommandSourceSelectableTag>(entity)}");

            if (engine.World.TryGet(entity, out CullState cull))
            {
                details.Add($"cullVisible={cull.IsVisible}");
                details.Add($"lod={cull.LOD}");
                details.Add($"coverage={cull.ScreenCoverage01:0.###}");
            }
            else
            {
                details.Add("cull=<missing>");
            }

            if (engine.World.TryGet(entity, out VisualTransform transform))
            {
                details.Add($"visual=({transform.Position.X:0.##},{transform.Position.Y:0.##},{transform.Position.Z:0.##})");
            }
            else
            {
                details.Add("visual=<missing>");
            }

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            bool hasBounds = SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect bounds);
            details.Add(hasBounds
                ? $"bounds=({bounds.MinX:0.##},{bounds.MinY:0.##})-({bounds.MaxX:0.##},{bounds.MaxY:0.##})"
                : "bounds=<none>");
            details.Add($"pointerHits={SpatialBoundsUtility.PointerHitsEntity(engine.World, entity, projector, screenPoint, 36f)}");

            if (engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input)
            {
                Vector2 pointer = input.ReadAction<Vector2>("PointerPos");
                details.Add($"inputPointer=({pointer.X:0.##},{pointer.Y:0.##})");
            }

            return string.Join(" | ", details);
        }

        private static string GetActiveModeId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreInputMod.ViewMode.ViewModeManager.ActiveModeIdKey, out var modeIdObj) && modeIdObj is string modeId
                ? modeId
                : string.Empty;
        }
        private static void CaptureSnapshot(
            GameEngine engine,
            UIRoot uiRoot,
            NarrativeDirector director,
            List<AcceptanceSnapshot> snapshots,
            List<UiAcceptanceEvidenceFrame> frames,
            IReadOnlyList<double> frameTimesMs,
            string screensDir,
            string step)
        {
            var entities = new List<EntityState>(TrackedNames.Length);
            for (int i = 0; i < TrackedNames.Length; i++) entities.Add(BuildEntityState(engine.World, TrackedNames[i]));

            (string when, string who, string what, string where, string why, string how) = GetEvidenceMetadata(step);
            UiAcceptanceEvidenceFrame frame = AcceptanceUiEvidenceWriter.CaptureFrame(
                uiRoot,
                screensDir,
                snapshots.Count + 1,
                step,
                when,
                who,
                what,
                where,
                why,
                how);
            frames.Add(frame);

            var snapshot = new AcceptanceSnapshot(
                step,
                frame.ScreenshotFileName,
                director.BuildQuestSummary(),
                director.BuildObjectiveSummary(),
                director.BuildDialogueSummary(),
                director.BuildCinematicSummary(),
                director.BuildVariableSummary(NarrativeShowcaseMod.NarrativeShowcaseIds.TrustVariableId, NarrativeShowcaseMod.NarrativeShowcaseIds.LoreVariableId, NarrativeShowcaseMod.NarrativeShowcaseIds.EndingVariableId),
                frame.UiHead,
                GetActiveModeId(engine),
                frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d,
                entities);
            snapshots.Add(snapshot);
        }

        private static EntityState BuildEntityState(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            if (entity == Entity.Null || !world.IsAlive(entity)) return new EntityState(name, false, 0f, 0f, 0f);
            Vector2 pos = ReadPosition(world, entity);
            return new EntityState(name, true, pos.X, pos.Y, ReadHealth(world, entity));
        }

        private static (string When, string Who, string What, string Where, string Why, string How) GetEvidenceMetadata(string step)
        {
            return step switch
            {
                "map_loaded" => ("T+001", "Arcweaver, Warden Mirelle, and the shared HUD", "Boot the showcase and confirm quest tracker, journal, variables, and prompt ribbon are mounted.", "narrative_showcase_hub", "Verify the reusable frontend is live before any branch begins.", "Load the real mod set, tick the engine, and snapshot the mounted UIRoot scene."),
                "intro_complete" => ("T+002", "Arcweaver and Warden Mirelle", "Advance the intro until the transmission overlay hands off into overlay dialogue.", "The shrine approach briefing inside the hub map.", "Show cinematic-to-dialogue handoff works through one shared frontend owner.", "Drive production input with Enter and wait for NarrativeDirector to enter dialogue state."),
                "briefing_branch_complete" => ("T+003", "Arcweaver, Mirelle, and the branch state panel", "Take the lore branch, unlock trust, and advance the quest into the trial stage.", "The elder dialogue flow with variables and objective tracker still visible.", "Prove conditions, variables, and choice availability remain data-driven.", "Commit choice shortcuts with number keys and validate NarrativeDirector variables plus quest stage."),
                "shrine_interacted" => ("T+004", "Arcweaver, the shrine, and the subtitle bubble", "Trigger the shrine reveal and capture the non-wait-input subtitle bubble state.", "The shrine arena on the trial objective.", "Validate the frontend supports skippable cinematic/subtitle beats without blocking on wait-input.", "Use ECS move and interaction loops to hit the trigger, then wait until the shared frontend mounts the subtitle bubble."),
                "beast_spawned" => ("T+005", "Arcweaver, the Ashen Beast, and the history journal", "Complete the reveal callback and show the spawned beast arriving while the flow review updates.", "The shrine arena after the cinematic signal resolves.", "Prove cinematic callbacks can wake gameplay entities and the frontend reflects the transition.", "Advance once, wait for the runtime entity spawn queue, and resnapshot the mounted frontend."),
                "beast_pressured" => ("T+006", "Arcweaver, the Ashen Beast, and the combat prompt ribbon", "Damage the newly spawned beast through the shared combat input before the deterministic finisher lands.", "The shrine arena while the return stage is still unresolved.", "Close the evidence gap between spawn and defeat with a real playable combat step.", "Aim at the beast, fire Arcweaver's inherited combat action, and snapshot the mounted frontend after health drops."),
                "beast_defeated" => ("T+007", "Arcweaver, the fallen beast, and the objective tracker", "Finish the encounter and show the quest state shifting into the return leg.", "The trial arena after combat resolution.", "Validate GAS combat, signals, and narrative callbacks converge without a parallel quest pipeline.", "Apply deterministic production effect requests until the beast dies and the return stage is published."),
                "mercy_ending" => ("T+008", "Arcweaver, Mirelle, and the completed quest surfaces", "Choose the Mercy ending and confirm the reward branch lands as shared frontend state.", "Back at the elder after the trial.", "Demonstrate branch gating on prior lore knowledge and trigger-driven reward callbacks.", "Return to Mirelle, pick the unlocked ending, and validate the reward raises MoveSpeed while quest status completes."),
                _ => ($"T+{step}", "Narrative showcase actors", "Capture the current narrative frontend state.", "The active showcase map.", "Keep the screenshot flow auditable.", "Snapshot the mounted UIRoot scene.")
            };
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
                    logical_time = $"T+{i + 1:000}",
                    step = snapshot.Step,
                    screenshot = snapshot.ScreenshotFileName,
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

        private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<AcceptanceSnapshot> snapshots, IReadOnlyList<UiAcceptanceEvidenceFrame> frames, IReadOnlyList<double> frameTimesMs)
        {
            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var final = snapshots[^1];
            string buildStamp = typeof(GameEngine).Assembly.GetName().Version?.ToString() ?? "unknown";
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: narrative-showcase");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario: `narrative-showcase`");
            sb.AppendLine($"- build: `GameEngine {buildStamp}`");
            sb.AppendLine($"- execution_timestamp_utc: `{DateTimeOffset.UtcNow:O}`");
            sb.AppendLine("- map: `narrative_showcase_hub`");
            sb.AppendLine("- clock: `fixed 1/60s`");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: play a full quest/dialogue/cinematic loop that starts in a camera-led intro, branches on dialogue knowledge, wakes a shrine, defeats a spawned beast, and returns for an ending choice.");
            sb.AppendLine("- Gameplay domain: shared Ludots ECS movement, interaction showcase combat/GAS, trigger callbacks, runtime entity spawning, virtual cameras, and a single reusable narrative frontend scene.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `narrative_showcase_hub`");
            sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
            sb.AppendLine("- Input source: real `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic backend injections.");
            sb.AppendLine("- Narrative branches exercised: briefing lore path -> return mercy path.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Boot the real engine with the narrative showcase mod and load the hub map.");
            sb.AppendLine("2. Advance the intro cinematic, then choose the lore branch and accept the trial in elder dialogue.");
            sb.AppendLine("3. Move to the shrine through the production order path and trigger the reveal callback.");
            sb.AppendLine("4. Damage the spawned beast through the inherited interaction combat input, then finish it through deterministic GAS effect application.");
            sb.AppendLine("5. Return to the elder, choose the Mercy ending, and validate the trigger-driven GAS blessing reward.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: quest, dialogue, cinematic, interaction, and reward callbacks stay on shared runtime infrastructure from start to finish.");
            sb.AppendLine("- Failure branch condition: without prior lore knowledge, the Mercy branch remains unavailable at return dialogue.");
            sb.AppendLine("- Key metrics: quest stage, trust/lore/ending variables, cinematic state, active UI surfaces, beast health, and reward movement speed delta.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/narrative-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/narrative-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/narrative-showcase/path.mmd`");
            sb.AppendLine("- `artifacts/acceptance/narrative-showcase/5w1h.md`");
            for (int i = 0; i < frames.Count; i++) sb.AppendLine($"- `artifacts/acceptance/narrative-showcase/screens/{frames[i].ScreenshotFileName}`");
            sb.AppendLine("- `artifacts/acceptance/narrative-showcase/screens/timeline.png`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++) sb.AppendLine($"- {timeline[i]}");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine($"- final quest: `{final.QuestSummary}`");
            sb.AppendLine($"- final variables: `{final.VariableSummary}`");
            sb.AppendLine($"- final dialogue card: `{final.DialogueSummary}`");
            sb.AppendLine("- reason: the showcase stayed on `ConfigPipeline`, `NarrativeDirector`, `TriggerManager`, `RuntimeEntitySpawnQueue`, `EffectRequestQueue`, `PlayerInputHandler`, `EntityCollectionContextRuntime`, and the shared `NarrativeFrontendMod` scene owner.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- total_actions: `{timeline.Count}`");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine($"- final_ui_excerpt: `{string.Join(" | ", final.UiText.Take(4))}`");
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
                "    J --> K{Lore learned?}",
                "    K -- no --> L[Guard branch: Mercy ending stays locked]",
                "    K -- yes --> M[Return dialogue unlocks Mercy branch]",
                "    M --> N[Reward signal applies GAS blessing and quest completes]"
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

        private sealed record AcceptanceSnapshot(string Step, string ScreenshotFileName, string QuestSummary, string ObjectiveSummary, string DialogueSummary, string CinematicSummary, string VariableSummary, IReadOnlyList<string> UiText, string ActiveModeId, double TickMs, IReadOnlyList<EntityState> Entities);
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

        private sealed class HeadlessCameraRuntime
        {
            public HeadlessCameraRuntime(CameraPresenter cameraPresenter, PresentationFrameSetupSystem? presentationFrameSetup)
            {
                CameraPresenter = cameraPresenter;
                PresentationFrameSetup = presentationFrameSetup;
            }

            public CameraPresenter CameraPresenter { get; }
            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        }

        private sealed class StubCameraAdapter : ICameraAdapter
        {
            public CameraRenderState3D LastState { get; private set; }

            public void UpdateCamera(in CameraRenderState3D state)
            {
                LastState = state;
            }
        }
    }
}
