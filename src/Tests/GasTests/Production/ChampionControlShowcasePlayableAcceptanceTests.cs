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
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ChampionControlShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ControlMapId = "champion_control_showcase";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;
        private const float WorldMinX = 2140f;
        private const float WorldMaxX = 3200f;
        private const float WorldMinY = 720f;
        private const float WorldMaxY = 1180f;
        private const string ControlCasterAutoPulseEnabledKey = "ChampionSkillSandbox.Control.CasterAutoPulseEnabled";
        private const string HeadlessCameraKey = "Tests.ChampionControlShowcase.HeadlessCamera";
        private const string TestInputBackendKey = "Tests.ChampionControlShowcase.InputBackend";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CommonControlBuffsMod",
            "CommonControlBuffsPresentationMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "DiagnosticsOverlayMod",
            "EntityCommandPanelMod",
            "ChampionSkillSandboxMod"
        };
        private static readonly Vector2[] HoverProbeOffsets =
        {
            Vector2.Zero,
            new Vector2(0f, -24f),
            new Vector2(0f, 24f),
            new Vector2(-24f, 0f),
            new Vector2(24f, 0f),
            new Vector2(-36f, -36f),
            new Vector2(36f, -36f),
            new Vector2(-36f, 36f),
            new Vector2(36f, 36f),
            new Vector2(0f, -48f),
            new Vector2(0f, 48f),
            new Vector2(-48f, 0f),
            new Vector2(48f, 0f),
            new Vector2(-64f, -24f),
            new Vector2(64f, -24f),
            new Vector2(-64f, 24f),
            new Vector2(64f, 24f)
        };

        [Test]
        public void ChampionControlShowcase_PlayableAcceptance_WritesArtifactsAndScreens()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "champion-control-showcase");
            string screensDir = Path.Combine(artifactDir, "screens");
            AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

            var timeline = new List<string>();
            var snapshots = new List<ControlShowcaseSnapshot>();
            var captureFrames = new List<UiAcceptanceEvidenceFrame>();
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            engine.GlobalContext[ControlCasterAutoPulseEnabledKey] = false;
            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var backend = GetInputBackend(engine);

            LoadMap(engine, ControlMapId, frameTimesMs);
            TickUntil(engine, frameTimesMs, () => engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) != null, 24);

            Entity marshal = FindEntityByName(engine.World, "Control Marshal");
            Entity runner = FindEntityByName(engine.World, "Control Runner");
            Entity caster = FindEntityByName(engine.World, "Control Caster");

            SelectEntity(engine, marshal, frameTimesMs);
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 1, "loaded", "Control showcase booted with the marshal selected.");
            timeline.Add("[T+001] Control showcase loaded | marshal selected | overlay exposes Q slow / W silence / E root / R stun");

            IssueMoveOrder(engine, backend, runner, new Vector2(3140f, 1060f), frameTimesMs);
            Vector2 runnerBaselineStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 24, frameTimesMs);
            float baselineRunnerTravel = Vector2.Distance(runnerBaselineStart, ReadPosition(engine.World, "Control Runner"));
            Assert.That(baselineRunnerTravel, Is.GreaterThan(60f));
            SubmitStopOrder(engine, runner);
            TickUntil(
                engine,
                frameTimesMs,
                () => !HasOutstandingOrders(engine.World, runner),
                24,
                () => BuildControlCastDebugSummary(engine, marshal, runner));

            CastSkillAtTarget(engine, backend, marshal, "<Keyboard>/q", runner, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(engine, runner, "Status.Slowed"),
                12,
                () => BuildControlCastDebugSummary(engine, marshal, runner));
            SelectEntity(engine, runner, frameTimesMs);
            IssueMoveOrder(engine, backend, runner, new Vector2(2180f, 1060f), frameTimesMs);
            Vector2 runnerSlowStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 12, frameTimesMs);
            float slowRunnerTravel = Vector2.Distance(runnerSlowStart, ReadPosition(engine.World, "Control Runner"));
            (float runnerSlowCurrent, float runnerSlowBase) = ReadMoveSpeed(engine.World, runner);
            Assert.That(
                HasEffectiveTag(engine, runner, "Status.Slowed"),
                Is.True,
                BuildControlCastDebugSummary(engine, marshal, runner));
            Assert.That(runnerSlowCurrent, Is.LessThan(runnerSlowBase));
            Assert.That(slowRunnerTravel, Is.LessThan(baselineRunnerTravel * 0.8f));
            SubmitStopOrder(engine, runner);
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 2, "slow", "Marshal Q applies a heavy slow through the MoveSpeed chain and preserves movement.");
            timeline.Add($"[T+002] Marshal Q -> Runner | Slow | MoveSpeed {runnerSlowBase:0}->{runnerSlowCurrent:0} | travel {baselineRunnerTravel:0.#}cm -> {slowRunnerTravel:0.#}cm");

            TickUntilFixedFrameBudget(
                engine,
                frameTimesMs,
                () => !HasEffectiveTag(engine, runner, "Status.Slowed"),
                120,
                () => BuildControlRecoveryDebugSummary(engine, runner));
            CastSkillAtTarget(engine, backend, marshal, "<Keyboard>/e", runner, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(engine, runner, "Status.Rooted"),
                12,
                () => BuildControlCastDebugSummary(engine, marshal, runner));
            SelectEntity(engine, runner, frameTimesMs);
            IssueMoveOrder(engine, backend, runner, new Vector2(3140f, 1060f), frameTimesMs);
            Vector2 runnerRootStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 18, frameTimesMs);
            Vector2 runnerRootEnd = ReadPosition(engine.World, "Control Runner");
            GameplayControlState rootedState = GameplayControlStateResolver.GetOrDefault(engine.World, runner);
            Assert.That(Vector2.Distance(runnerRootStart, runnerRootEnd), Is.LessThanOrEqualTo(8f));
            Assert.That(HasEffectiveTag(engine, runner, "Status.Rooted"), Is.True);
            Assert.That(rootedState.IsMoveBlocked(), Is.True);
            Assert.That(engine.World.Get<NavKinematics2D>(runner).MaxSpeedCmPerSec.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 3, "root", "Marshal E projects move-block through the control-state sink.");
            timeline.Add("[T+003] Marshal E -> Runner | Root | MoveBlocked active | control sink drives nav max speed to 0");

            TickUntilFixedFrameBudget(
                engine,
                frameTimesMs,
                () => !HasEffectiveTag(engine, runner, "Status.Rooted"),
                90,
                () => BuildControlRecoveryDebugSummary(engine, runner));
            SelectEntity(engine, runner, frameTimesMs);
            IssueMoveOrder(engine, backend, runner, new Vector2(2180f, 1060f), frameTimesMs);
            Vector2 runnerRecoverStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 24, frameTimesMs);
            float runnerRecoverTravel = Vector2.Distance(runnerRecoverStart, ReadPosition(engine.World, "Control Runner"));
            Assert.That(runnerRecoverTravel, Is.GreaterThan(40f));
            SubmitStopOrder(engine, runner);
            TickUntil(
                engine,
                frameTimesMs,
                () => !HasOutstandingOrders(engine.World, runner),
                24,
                () => BuildControlRecoveryDebugSummary(engine, runner));

            CastSkillAtTarget(engine, backend, marshal, "<Keyboard>/r", runner, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(engine, runner, "Status.Stunned"),
                12,
                () => BuildControlCastDebugSummary(engine, marshal, runner));
            SelectEntity(engine, runner, frameTimesMs);
            IssueMoveOrder(engine, backend, runner, new Vector2(3140f, 1060f), frameTimesMs);
            Vector2 runnerStunStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 18, frameTimesMs);
            Vector2 runnerStunEnd = ReadPosition(engine.World, "Control Runner");
            GameplayControlState stunnedRunnerState = GameplayControlStateResolver.GetOrDefault(engine.World, runner);
            Assert.That(Vector2.Distance(runnerStunStart, runnerStunEnd), Is.LessThanOrEqualTo(24f));
            Assert.That(HasEffectiveTag(engine, runner, "Status.Stunned"), Is.True);
            Assert.That(stunnedRunnerState.IsMoveBlocked(), Is.True);
            Assert.That(stunnedRunnerState.ActionBlocked, Is.EqualTo((byte)1));
            Assert.That(engine.World.Get<NavKinematics2D>(runner).MaxSpeedCmPerSec.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 4, "stun_runner", "Marshal R blocks action and movement through the shared reusable mod.");
            timeline.Add("[T+004] Marshal R -> Runner | Stun | ActionBlocked=1 | movement and action both gated");

            TickUntilFixedFrameBudget(
                engine,
                frameTimesMs,
                () => !HasEffectiveTag(engine, runner, "Status.Stunned"),
                120,
                () => BuildControlRecoveryDebugSummary(engine, runner));
            TickUntil(engine, frameTimesMs, () => !HasAbilityExec(engine.World, caster), 48);
            float marshalHealthBeforeBaselineCast = ReadHealth(engine.World, "Control Marshal");
            CastSkillAtTarget(engine, backend, caster, "<Keyboard>/q", marshal, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasAbilityExec(engine.World, caster),
                24,
                () => BuildArcPulseDebugSummary(engine, caster, marshal));
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadHealth(engine.World, "Control Marshal") < marshalHealthBeforeBaselineCast,
                80,
                () => BuildArcPulseDebugSummary(engine, caster, marshal));
            float marshalHealthAfterBaselineCast = ReadHealth(engine.World, "Control Marshal");
            Assert.That(marshalHealthAfterBaselineCast, Is.EqualTo(marshalHealthBeforeBaselineCast - 10f).Within(0.001f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 5, "baseline_cast", "Caster baseline cast lands before control gates are applied.");
            timeline.Add($"[T+005] Caster -> Marshal | Arc Pulse hit | HP {marshalHealthBeforeBaselineCast:0}->{marshalHealthAfterBaselineCast:0}");

            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(engine, caster, "Cooldown.ControlShowcase.Caster.Q"), 180);
            CastSkillAtTarget(engine, backend, marshal, "<Keyboard>/w", caster, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(engine, caster, "Status.Silenced"),
                12,
                () => BuildControlCastDebugSummary(engine, marshal, caster));
            SelectEntity(engine, caster, frameTimesMs);
            float marshalHealthBeforeSilence = ReadHealth(engine.World, "Control Marshal");
            CastSkillAtTarget(engine, backend, caster, "<Keyboard>/q", marshal, frameTimesMs);
            Tick(engine, 4, frameTimesMs);
            GameplayControlState silencedCasterState = GameplayControlStateResolver.GetOrDefault(engine.World, caster);
            Assert.That(HasEffectiveTag(engine, caster, "Status.Silenced"), Is.True);
            Assert.That(HasAbilityExec(engine.World, caster), Is.False);
            Assert.That(HasEffectiveTag(engine, caster, "Cooldown.ControlShowcase.Caster.Q"), Is.False);
            Assert.That(ReadHealth(engine.World, "Control Marshal"), Is.EqualTo(marshalHealthBeforeSilence).Within(0.001f));
            Assert.That(silencedCasterState.ActionBlocked, Is.EqualTo((byte)1));
            Assert.That(silencedCasterState.IsMoveBlocked(), Is.False);
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 6, "silence", "Marshal W projects action-block without affecting movement.");
            timeline.Add("[T+006] Marshal W -> Caster | Silence | cast startup rejected before exec starts");

            TickUntilFixedFrameBudget(engine, frameTimesMs, () => !HasEffectiveTag(engine, caster, "Status.Silenced"), 108);
            TickUntil(engine, frameTimesMs, () => !HasAbilityExec(engine.World, caster), 48);
            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(engine, caster, "Cooldown.ControlShowcase.Caster.Q"), 180);

            CastSkillAtTarget(engine, backend, caster, "<Keyboard>/q", marshal, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasAbilityExec(engine.World, caster),
                24,
                () => BuildArcPulseDebugSummary(engine, caster, marshal));
            float marshalHealthBeforeStunInterrupt = ReadHealth(engine.World, "Control Marshal");
            CastSkillAtTarget(engine, backend, marshal, "<Keyboard>/r", caster, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(engine, caster, "Status.Stunned"),
                12,
                () => BuildControlCastDebugSummary(engine, marshal, caster));
            Tick(engine, 18, frameTimesMs);
            GameplayControlState stunnedCasterState = GameplayControlStateResolver.GetOrDefault(engine.World, caster);
            Assert.That(HasEffectiveTag(engine, caster, "Status.Stunned"), Is.True);
            Assert.That(HasAbilityExec(engine.World, caster), Is.False);
            Assert.That(ReadHealth(engine.World, "Control Marshal"), Is.EqualTo(marshalHealthBeforeStunInterrupt).Within(0.001f));
            Assert.That(stunnedCasterState.ActionBlocked, Is.EqualTo((byte)1));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 7, "stun_interrupt", "Marshal R interrupts an active cast and blocks follow-up action.");
            timeline.Add("[T+007] Caster starts Arc Pulse -> Marshal R interrupts mid-cast | active exec cancelled before damage resolves");

            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs, baselineRunnerTravel, slowRunnerTravel, runnerRecoverTravel));
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            AcceptanceUiEvidenceWriter.WriteTimelineSheet(
                captureFrames,
                screensDir,
                Path.Combine(screensDir, "timeline.png"),
                "Champion control showcase acceptance evidence timeline");
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallUi(engine);
            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            var cameraAdapter = new StubCameraAdapter();
            var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view, timingDiagnostics);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext[HeadlessCameraKey] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup));
            engine.Start();
            return engine;
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
            backend.SetMousePosition(new Vector2(960f, 540f));
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void InstallUi(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 12)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
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
            Func<string>? failureMessageFactory = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            string failureMessage = failureMessageFactory?.Invoke() ?? "No diagnostic details captured.";
            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames. {failureMessage}");
        }

        private static void TickUntilFixedFrameBudget(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> predicate,
            int maxFixedFrames,
            Func<string>? failureMessageFactory = null)
        {
            int startFixedFrame = ReadClock(engine, ClockDomainId.FixedFrame);
            int maxRenderFrames = maxFixedFrames * 6 + 30;
            for (int i = 0; i < maxRenderFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                if (ReadClock(engine, ClockDomainId.FixedFrame) - startFixedFrame >= maxFixedFrames)
                {
                    break;
                }

                Tick(engine, 1, frameTimesMs);
            }

            string failureMessage = failureMessageFactory?.Invoke() ?? "No diagnostic details captured.";
            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFixedFrames} fixed frames. {failureMessage}");
        }

        private static void SelectEntity(GameEngine engine, Entity target, List<double> frameTimesMs)
        {
            SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime missing.");
            Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (engine.World.TryGet(target, out PlayerOwner targetOwner) && targetOwner.PlayerId == 1)
            {
                owner = target;
                engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
            }
            else if (!engine.World.IsAlive(owner))
            {
                owner = target;
                engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
            }

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, next);
            Tick(engine, 1, frameTimesMs);
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[TestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Control showcase test input backend is missing.");
        }

        private static void CastSkillAtTarget(
            GameEngine engine,
            TestInputBackend backend,
            Entity actor,
            string buttonPath,
            Entity target,
            List<double> frameTimesMs)
        {
            SelectEntity(engine, actor, frameTimesMs);
            string targetName = GetEntityName(engine.World, target);
            Vector2 targetScreen = GetGroundScreenFromWorld(engine, ReadPosition(engine.World, targetName));
            if (TryFindHoverScreenPoint(engine, backend, targetName, GetEntityScreen(engine, target), frameTimesMs, out Vector2 hoveredPoint))
            {
                targetScreen = hoveredPoint;
            }
            SetMouseWorld(engine, backend, targetScreen, frameTimesMs);
            PressButton(engine, backend, buttonPath, frameTimesMs);
        }

        private static void IssueMoveOrder(
            GameEngine engine,
            TestInputBackend backend,
            Entity actor,
            Vector2 targetWorldCm,
            List<double> frameTimesMs)
        {
            SelectEntity(engine, actor, frameTimesMs);
            RightClickWorld(engine, backend, GetGroundScreenFromWorld(engine, targetWorldCm), frameTimesMs);
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(path, false);
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

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(HeadlessCameraKey, out object? runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
        }

        private static Vector2 GetEntityScreen(GameEngine engine, Entity entity)
        {
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            if (engine.World.TryGet(entity, out VisualTransform transform))
            {
                return projector.WorldToScreen(transform.Position);
            }

            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            return projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static Vector2 GetGroundScreenFromWorld(GameEngine engine, Vector2 worldCm)
        {
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            return projector.WorldToScreen(new Vector3(WorldUnits.CmToM(worldCm.X), 0f, WorldUnits.CmToM(worldCm.Y)));
        }

        private static Vector2 FindHoverScreenPoint(
            GameEngine engine,
            TestInputBackend backend,
            string entityName,
            Vector2 projectedScreenPoint,
            List<double> frameTimesMs)
        {
            if (TryFindHoverScreenPoint(engine, backend, entityName, projectedScreenPoint, frameTimesMs, out Vector2 matchedPoint))
            {
                return matchedPoint;
            }

            Vector2 groundProjectedPoint = GetGroundScreenFromWorld(engine, ReadPosition(engine.World, entityName));
            if (Vector2.Distance(groundProjectedPoint, projectedScreenPoint) > 1f &&
                TryFindHoverScreenPoint(engine, backend, entityName, groundProjectedPoint, frameTimesMs, out matchedPoint))
            {
                return matchedPoint;
            }

            Assert.Fail(
                $"Failed to find hover point for '{entityName}' near projected point ({projectedScreenPoint.X:0.0},{projectedScreenPoint.Y:0.0}) and ground point ({groundProjectedPoint.X:0.0},{groundProjectedPoint.Y:0.0}).");
            return default;
        }

        private static bool TryFindHoverScreenPoint(
            GameEngine engine,
            TestInputBackend backend,
            string entityName,
            Vector2 projectedScreenPoint,
            List<double> frameTimesMs,
            out Vector2 matchedPoint)
        {
            const int hoverSearchRounds = 6;
            for (int round = 0; round < hoverSearchRounds; round++)
            {
                for (int i = 0; i < HoverProbeOffsets.Length; i++)
                {
                    Vector2 liveProjectedPoint = projectedScreenPoint;
                    Vector2 refreshedPoint = GetEntityScreen(engine, FindEntityByName(engine.World, entityName));
                    if (!float.IsNaN(refreshedPoint.X) &&
                        !float.IsInfinity(refreshedPoint.X) &&
                        !float.IsNaN(refreshedPoint.Y) &&
                        !float.IsInfinity(refreshedPoint.Y))
                    {
                        liveProjectedPoint = refreshedPoint;
                    }

                    Vector2 candidate = liveProjectedPoint + HoverProbeOffsets[i];
                    backend.SetMousePosition(candidate);
                    Tick(engine, 1, frameTimesMs);
                    if (string.Equals(ReadHoveredEntityName(engine), entityName, StringComparison.Ordinal))
                    {
                        matchedPoint = candidate;
                        return true;
                    }
                }
            }

            matchedPoint = default;
            return false;
        }

        private static string ReadHoveredEntityName(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out object? hoveredObj) &&
                   hoveredObj is Entity hovered &&
                   hovered != Entity.Null &&
                   engine.World.TryGet(hovered, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static string GetEntityName(World world, Entity entity)
        {
            return world.TryGet(entity, out Name name)
                ? name.Value
                : $"Entity#{entity.Id}";
        }

        private static float ReadDistance(World world, Entity a, Entity b)
        {
            if (!world.TryGet(a, out WorldPositionCm aPosition) || !world.TryGet(b, out WorldPositionCm bPosition))
            {
                return float.PositiveInfinity;
            }

            var aWorld = aPosition.ToWorldCmInt2();
            var bWorld = bPosition.ToWorldCmInt2();
            Vector2 pa = new(aWorld.X, aWorld.Y);
            Vector2 pb = new(bWorld.X, bWorld.Y);
            return Vector2.Distance(pa, pb);
        }

        private static bool HasOutstandingOrders(World world, Entity entity)
        {
            return world.TryGet(entity, out OrderBuffer orders) &&
                   (orders.HasActive || orders.HasPending || orders.HasQueued);
        }

        private static void SubmitStopOrder(GameEngine engine, Entity actor)
        {
            OrderQueue orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("OrderQueue missing.");
            var order = new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["stop"],
                PlayerId = 1,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate
            };

            Assert.That(orderQueue.TryEnqueueAssigned(ref order), Is.True);
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            ScreenOverlayBuffer overlay,
            List<ControlShowcaseSnapshot> snapshots,
            List<UiAcceptanceEvidenceFrame> captureFrames,
            string screensDir,
            int frameIndex,
            string step,
            string note)
        {
            Entity marshal = FindEntityByName(engine.World, "Control Marshal");
            Entity runner = FindEntityByName(engine.World, "Control Runner");
            Entity caster = FindEntityByName(engine.World, "Control Caster");

            var snapshot = new ControlShowcaseSnapshot(
                frameIndex,
                step,
                note,
                GetSelectedEntityName(engine),
                ReadOverlayEvidenceLines(overlay, GetSelectedEntityName(engine)),
                ReadActorSnapshot(engine, marshal),
                ReadActorSnapshot(engine, runner),
                ReadActorSnapshot(engine, caster));
            snapshots.Add(snapshot);

            string fileName = $"{frameIndex:000}_{step}.png";
            AcceptanceStageEvidenceWriter.WriteFrame(
                BuildControlEvidenceFrame(snapshot),
                Path.Combine(screensDir, fileName));
            captureFrames.Add(BuildControlTimelineFrame(snapshot, fileName));
        }

        private static ActorSnapshot ReadActorSnapshot(GameEngine engine, Entity entity)
        {
            string name = engine.World.TryGet(entity, out Name actorName) ? actorName.Value : $"Entity#{entity.Id}";
            Vector2 position = ReadPosition(engine.World, name);
            (float health, float maxHealth) = ReadHealthState(engine.World, name);
            (float currentMoveSpeed, float baseMoveSpeed) = ReadMoveSpeed(engine.World, entity);
            GameplayControlState controlState = GameplayControlStateResolver.GetOrDefault(engine.World, entity);
            return new ActorSnapshot(
                name,
                position.X,
                position.Y,
                health,
                maxHealth,
                currentMoveSpeed,
                baseMoveSpeed,
                ReadTagSummary(engine, entity),
                controlState.IsMoveBlocked(),
                controlState.ActionBlocked != 0,
                HasAbilityExec(engine.World, entity),
                ReadExecState(engine.World, entity));
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                   engine.World.TryGet(selected, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static IReadOnlyList<string> ReadOverlayEvidenceLines(ScreenOverlayBuffer overlay, string selectedEntity)
        {
            var lines = new List<string>(8);
            if (!string.IsNullOrWhiteSpace(selectedEntity))
            {
                lines.Add($"Selected {selectedEntity}");
            }

            foreach (ref readonly var item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string trimmed = text.Trim();
                    if (trimmed.StartsWith("Selected ", StringComparison.Ordinal) ||
                        trimmed.StartsWith("Mode ", StringComparison.Ordinal) ||
                        trimmed.Contains("Melee Context Showcase", StringComparison.Ordinal) ||
                        trimmed.Contains("Duelist Alpha", StringComparison.Ordinal) ||
                        trimmed.Contains("Hover a dummy", StringComparison.Ordinal) ||
                        trimmed.Contains("Click Duelist Alpha", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (trimmed.Contains("Control Showcase", StringComparison.Ordinal) ||
                        trimmed.Contains("Q slow", StringComparison.Ordinal) ||
                        trimmed.Contains("W silence", StringComparison.Ordinal) ||
                        trimmed.Contains("E root", StringComparison.Ordinal) ||
                        trimmed.Contains("R stun", StringComparison.Ordinal) ||
                        trimmed.StartsWith("Tags ", StringComparison.Ordinal))
                    {
                        lines.Add(trimmed);
                    }
                }
            }

            return lines
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToArray();
        }

        private static string ReadTagSummary(GameEngine engine, Entity entity)
        {
            var labels = new List<string>(6);
            AddTagIfActive(engine, entity, labels, "Status.Slowed", "Slowed");
            AddTagIfActive(engine, entity, labels, "Status.Rooted", "Rooted");
            AddTagIfActive(engine, entity, labels, "Status.Stunned", "Stunned");
            AddTagIfActive(engine, entity, labels, "Status.Silenced", "Silenced");
            AddTagIfActive(engine, entity, labels, "Status.CannotMove", "CannotMove");
            AddTagIfActive(engine, entity, labels, "Status.CannotCast", "CannotCast");
            AddTagIfActive(engine, entity, labels, "Cooldown.ControlShowcase.Caster.Q", "CooldownQ");
            return labels.Count == 0 ? "(none)" : string.Join(", ", labels);
        }

        private static void AddTagIfActive(GameEngine engine, Entity entity, List<string> labels, string tagName, string label)
        {
            if (HasEffectiveTag(engine, entity, tagName))
            {
                labels.Add(label);
            }
        }

        private static bool HasEffectiveTag(GameEngine engine, Entity entity, string tagName)
        {
            int tagId = TagRegistry.GetId(tagName);
            if (tagId <= 0 || !engine.World.TryGet(entity, out GameplayTagContainer tags))
            {
                return false;
            }

            TagOps? tagOps = engine.GetService(CoreServiceKeys.TagOps);
            return tagOps != null
                ? tagOps.HasTag(ref tags, tagId, TagSense.Effective)
                : tags.HasTag(tagId);
        }

        private static bool HasAbilityExec(World world, Entity entity)
        {
            return world.IsAlive(entity) && world.Has<AbilityExecInstance>(entity);
        }

        private static string ReadExecState(World world, Entity entity)
        {
            return world.TryGet(entity, out AbilityExecInstance exec)
                ? exec.State.ToString()
                : "Idle";
        }

        private static string BuildControlRecoveryDebugSummary(GameEngine engine, Entity entity)
        {
            string name = engine.World.TryGet(entity, out Name actorName) ? actorName.Value : $"Entity#{entity.Id}";
            (float currentMoveSpeed, float baseMoveSpeed) = ReadMoveSpeed(engine.World, entity);
            GameplayControlState controlState = GameplayControlStateResolver.GetOrDefault(engine.World, entity);
            string tags = ReadTagSummary(engine, entity);
            string effects = ReadActiveEffectSummary(engine.World, entity);
            string clocks = ReadClockSummary(engine);
            string orders = ReadOrderBufferSummary(engine.World, entity);
            return $"Entity={name}; Tags={tags}; MoveSpeed={currentMoveSpeed:0.#}/{baseMoveSpeed:0.#}; MoveBlocked={controlState.IsMoveBlocked()}; ActionBlocked={controlState.ActionBlocked != 0}; Clocks={clocks}; ActiveEffects={effects}; Orders={orders}";
        }

        private static string BuildArcPulseDebugSummary(GameEngine engine, Entity caster, Entity marshal)
        {
            ActorSnapshot casterSnapshot = ReadActorSnapshot(engine, caster);
            ActorSnapshot marshalSnapshot = ReadActorSnapshot(engine, marshal);
            return $"Clocks={ReadClockSummary(engine)}; Caster={FormatActorSnapshot(casterSnapshot)}; Marshal={FormatActorSnapshot(marshalSnapshot)}; CasterOrders={ReadOrderBufferSummary(engine.World, caster)}; CasterBlackboard={ReadCastBlackboardSummary(engine.World, caster)}; CasterSlots={ReadAbilitySlotSummary(engine.World, caster)}; RecentCastEvents={ReadRecentCastEventSummary(engine, caster)}";
        }

        private static string BuildControlCastDebugSummary(GameEngine engine, Entity actor, Entity target)
        {
            ActorSnapshot actorSnapshot = ReadActorSnapshot(engine, actor);
            ActorSnapshot targetSnapshot = ReadActorSnapshot(engine, target);
            string lastOrder = engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastOrder", out object? orderObj)
                ? orderObj?.ToString() ?? "<null>"
                : "<missing>";
            string lastGround = engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastGroundWorldCm", out object? groundObj)
                ? groundObj?.ToString() ?? "<null>"
                : "<missing>";
            return $"Clocks={ReadClockSummary(engine)}; Hovered={ReadHoveredEntityName(engine)}; Selected={GetSelectedEntityName(engine)}; Local={ReadLocalPlayerName(engine)}; LastGround={lastGround}; LastOrder={lastOrder}; Actor={FormatActorSnapshot(actorSnapshot)}; Target={FormatActorSnapshot(targetSnapshot)}; AutoTargets={ReadAutoTargetCandidateSummary(engine, actor, 760)}; ActorOrders={ReadOrderBufferSummary(engine.World, actor)}; ActorBlackboard={ReadCastBlackboardSummary(engine.World, actor)}; ActorSlots={ReadAbilitySlotSummary(engine.World, actor)}; RecentCastEvents={ReadRecentCastEventSummary(engine, actor)}";
        }

        private static string ReadOrderBufferSummary(World world, Entity entity)
        {
            if (!world.TryGet(entity, out OrderBuffer orders))
            {
                return "(missing)";
            }

            string active = orders.HasActive
                ? $"active[type={orders.ActiveOrder.Order.OrderTypeId},id={orders.ActiveOrder.Order.OrderId},slot={orders.ActiveOrder.Order.Args.I0},target={orders.ActiveOrder.Order.Target.Id},mode={orders.ActiveOrder.Order.SubmitMode}]"
                : "active=(none)";
            string pending = orders.HasPending
                ? $"pending[type={orders.PendingOrder.Order.OrderTypeId},slot={orders.PendingOrder.Order.Args.I0},target={orders.PendingOrder.Order.Target.Id}]"
                : "pending=(none)";
            string queued = orders.HasQueued
                ? string.Join(
                    ",",
                    Enumerable.Range(0, orders.QueuedCount)
                        .Select(index =>
                        {
                            QueuedOrder queuedOrder = orders.GetQueued(index);
                            return $"q{index}[type={queuedOrder.Order.OrderTypeId},slot={queuedOrder.Order.Args.I0},target={queuedOrder.Order.Target.Id},mode={queuedOrder.Order.SubmitMode}]";
                        }))
                : "(none)";
            return $"{active}; {pending}; queued={queued}";
        }

        private static string ReadCastBlackboardSummary(World world, Entity entity)
        {
            string slot = world.TryGet(entity, out BlackboardIntBuffer ints) && ints.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex)
                ? slotIndex.ToString()
                : "(missing)";
            string target = world.TryGet(entity, out BlackboardEntityBuffer entities) && entities.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out Entity targetEntity)
                ? targetEntity.Id.ToString()
                : "(missing)";
            string position = "(missing)";
            if (world.TryGet(entity, out BlackboardSpatialBuffer spatial))
            {
                int pointCount = spatial.GetPointCount(OrderBlackboardKeys.Cast_TargetPosition);
                if (pointCount > 0 &&
                    spatial.TryGetPointAt(OrderBlackboardKeys.Cast_TargetPosition, pointCount - 1, out Vector3 point))
                {
                    position = $"{point.X:0.#},{point.Z:0.#} ({pointCount}pt)";
                }
            }

            return $"slot={slot}; target={target}; pos={position}";
        }

        private static string ReadAbilitySlotSummary(World world, Entity entity)
        {
            if (!world.TryGet(entity, out AbilityStateBuffer abilities))
            {
                return "(missing)";
            }

            bool hasForm = world.Has<AbilityFormSlotBuffer>(entity);
            AbilityFormSlotBuffer formSlots = hasForm ? world.Get<AbilityFormSlotBuffer>(entity) : default;
            bool hasItemGranted = world.Has<ItemGrantedSlotBuffer>(entity);
            ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? world.Get<ItemGrantedSlotBuffer>(entity) : default;
            bool hasGranted = world.Has<GrantedSlotBuffer>(entity);
            GrantedSlotBuffer grantedSlots = hasGranted ? world.Get<GrantedSlotBuffer>(entity) : default;

            return string.Join(
                ",",
                Enumerable.Range(0, abilities.Count)
                    .Select(index =>
                    {
                        AbilitySlotState slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in itemGrantedSlots, hasItemGranted, in grantedSlots, hasGranted, index);
                        return $"slot{index}[ability={slot.AbilityId},template={slot.TemplateEntityId}]";
                    }));
        }

        private static string ReadRecentCastEventSummary(GameEngine engine, Entity actor)
        {
            PresentationEventStream? stream = engine.GetService(CoreServiceKeys.PresentationEventStream);
            if (stream == null || stream.Count == 0)
            {
                return "(none)";
            }

            var matches = new List<string>(6);
            ReadOnlySpan<PresentationEvent> events = stream.GetSpan();
            for (int i = events.Length - 1; i >= 0 && matches.Count < 6; i--)
            {
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Source != actor ||
                    (evt.Kind != PresentationEventKind.CastCommitted && evt.Kind != PresentationEventKind.CastFailed))
                {
                    continue;
                }

                string payload = evt.Kind == PresentationEventKind.CastFailed
                    ? ((AbilityCastFailReason)evt.PayloadB).ToString()
                    : $"ability={evt.PayloadB}";
                matches.Add($"tick={evt.LogicTickStamp}:{evt.Kind}[slot={evt.PayloadA},{payload}]");
            }

            return matches.Count == 0 ? "(none)" : string.Join(", ", matches);
        }

        private static string ReadActiveEffectSummary(World world, Entity entity)
        {
            if (!world.TryGet(entity, out ActiveEffectContainer activeEffects) || activeEffects.Count == 0)
            {
                return "(none)";
            }

            int rootTemplateId = EffectTemplateIdRegistry.GetId("Effect.Control.Common.Root");
            int slowTemplateId = EffectTemplateIdRegistry.GetId("Effect.Control.Common.Slow.Heavy");
            int silenceTemplateId = EffectTemplateIdRegistry.GetId("Effect.Control.Common.Silence");
            int stunTemplateId = EffectTemplateIdRegistry.GetId("Effect.Control.Common.Stun");
            var parts = new List<string>(activeEffects.Count);
            for (int i = 0; i < activeEffects.Count; i++)
            {
                Entity effectEntity = activeEffects.GetEntity(i);
                if (!world.IsAlive(effectEntity) ||
                    !world.TryGet(effectEntity, out GameplayEffect effect) ||
                    !world.TryGet(effectEntity, out EffectContext context))
                {
                    continue;
                }

                string templateLabel = "Unknown";
                if (world.TryGet(effectEntity, out EffectTemplateRef templateRef))
                {
                    templateLabel = templateRef.TemplateId switch
                    {
                        var id when id == rootTemplateId => "Root",
                        var id when id == slowTemplateId => "HeavySlow",
                        var id when id == silenceTemplateId => "Silence",
                        var id when id == stunTemplateId => "Stun",
                        _ => $"Template#{templateRef.TemplateId}"
                    };
                }

                int stackCount = world.TryGet(effectEntity, out EffectStack stack) ? stack.Count : 1;
                parts.Add(
                    $"{templateLabel}[state={effect.State},remaining={effect.RemainingTicks},expiresAt={effect.ExpiresAtTick},target={context.Target.Id},stacks={stackCount}]");
            }

            return parts.Count == 0 ? "(none)" : string.Join("; ", parts);
        }

        private static string ReadClockSummary(GameEngine engine)
        {
            string fixedFrame = "?";
            string step = "?";
            if (engine.GetService(CoreServiceKeys.Clock) is IClock clock)
            {
                fixedFrame = clock.Now(ClockDomainId.FixedFrame).ToString();
                step = clock.Now(ClockDomainId.Step).ToString();
            }

            string mode = "?";
            string scale = "?";
            string stepEveryFixed = "?";
            if (engine.GetService(CoreServiceKeys.GasClockStepPolicy) is GasClockStepPolicy policy)
            {
                mode = policy.Mode.ToString();
                scale = policy.ScalePermille.ToString();
                stepEveryFixed = policy.StepEveryFixedTicks.ToString();
            }

            return $"fixed={fixedFrame},step={step},mode={mode},scale={scale},stepEveryFixed={stepEveryFixed}";
        }

        private static int ReadClock(GameEngine engine, ClockDomainId domain)
        {
            return engine.GetService(CoreServiceKeys.Clock) is IClock clock
                ? clock.Now(domain)
                : 0;
        }

        private static string FormatActorSnapshot(ActorSnapshot snapshot)
        {
            return $"{snapshot.Name}[pos={snapshot.PositionX:0.#},{snapshot.PositionY:0.#},hp={snapshot.Health:0.#},move={snapshot.MoveSpeedCurrent:0.#}/{snapshot.MoveSpeedBase:0.#},tags={snapshot.Tags},moveBlocked={snapshot.MoveBlocked},actionBlocked={snapshot.ActionBlocked},exec={snapshot.HasExec},execState={snapshot.ExecState}]";
        }

        private static string ReadLocalPlayerName(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                   localObj is Entity local &&
                   engine.World.TryGet(local, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static string ReadAutoTargetCandidateSummary(GameEngine engine, Entity actor, int rangeCm)
        {
            var spatial = engine.GetService(CoreServiceKeys.SpatialQueryService);
            if (spatial == null || !engine.World.TryGet(actor, out WorldPositionCm actorPosition))
            {
                return "(unavailable)";
            }

            int actorTeam = engine.World.TryGet(actor, out Team actorTeamComponent) ? actorTeamComponent.Id : 0;
            Span<Entity> candidates = stackalloc Entity[32];
            var result = spatial.QueryRadius(actorPosition.ToWorldCmInt2(), rangeCm, candidates);
            if (result.Count <= 0)
            {
                return "(none)";
            }

            var parts = new List<string>(result.Count);
            for (int i = 0; i < result.Count; i++)
            {
                Entity candidate = candidates[i];
                if (!engine.World.IsAlive(candidate) || candidate == actor || !engine.World.TryGet(candidate, out WorldPositionCm candidatePosition))
                {
                    continue;
                }

                string name = engine.World.TryGet(candidate, out Name candidateName) ? candidateName.Value : $"Entity#{candidate.Id}";
                int team = engine.World.TryGet(candidate, out Team candidateTeam) ? candidateTeam.Id : 0;
                float distance = Vector2.Distance(
                    new Vector2(actorPosition.Value.X.ToFloat(), actorPosition.Value.Y.ToFloat()),
                    new Vector2(candidatePosition.Value.X.ToFloat(), candidatePosition.Value.Y.ToFloat()));
                bool hostile = RelationshipFilterUtil.Passes(RelationshipFilter.Hostile, actorTeam, team);
                parts.Add($"{name}[team={team},dist={distance:0.#},hostile={hostile}]");
            }

            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }

        private static (float Current, float Base) ReadMoveSpeed(World world, Entity entity)
        {
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            if (!world.TryGet(entity, out AttributeBuffer attributes))
            {
                return (0f, 0f);
            }

            return (attributes.GetCurrent(moveSpeedId), attributes.GetBase(moveSpeedId));
        }

        private static Vector2 ReadPosition(World world, string entityName)
        {
            Entity entity = FindEntityByName(world, entityName);
            Assert.That(world.TryGet(entity, out WorldPositionCm position), Is.True);
            var worldCm = position.ToWorldCmInt2();
            return new Vector2(worldCm.X, worldCm.Y);
        }

        private static float ReadHealth(World world, string entityName)
        {
            return ReadHealthState(world, entityName).Current;
        }

        private static (float Current, float Max) ReadHealthState(World world, string entityName)
        {
            Entity entity = FindEntityByName(world, entityName);
            int healthId = AttributeRegistry.GetId("Health");
            Assert.That(healthId, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.TryGet(entity, out AttributeBuffer attributes), Is.True);
            return (attributes.GetCurrent(healthId), attributes.GetBase(healthId));
        }

        private static Entity FindEntityByName(World world, string entityName)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (found == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{entityName}' should exist on {ControlMapId}.");
            return found;
        }

        private static StageEvidenceFrame BuildControlEvidenceFrame(ControlShowcaseSnapshot snapshot)
        {
            IReadOnlyList<string> summaryLines = new[]
            {
                snapshot.Note,
                $"Runner {snapshot.Runner.MoveSpeedCurrent:0.#}/{snapshot.Runner.MoveSpeedBase:0.#} | moveBlocked={snapshot.Runner.MoveBlocked}",
                $"Caster exec {(snapshot.Caster.HasExec ? snapshot.Caster.ExecState : "Idle")} | actionBlocked={snapshot.Caster.ActionBlocked}"
            };
            IReadOnlyList<string> detailLines = new[]
            {
                $"{snapshot.Marshal.Name} | HP {snapshot.Marshal.Health:0}/{snapshot.Marshal.MaxHealth:0} | Tags {snapshot.Marshal.Tags}",
                $"{snapshot.Runner.Name} | HP {snapshot.Runner.Health:0}/{snapshot.Runner.MaxHealth:0} | Tags {snapshot.Runner.Tags}",
                $"{snapshot.Caster.Name} | HP {snapshot.Caster.Health:0}/{snapshot.Caster.MaxHealth:0} | Tags {snapshot.Caster.Tags}"
            }.Concat(snapshot.OverlayLines.Take(10).Select(line => $"Overlay | {TrimForPaint(line, 68)}")).ToArray();
            IReadOnlyList<StageEvidenceActor> actors = new[]
            {
                new StageEvidenceActor(snapshot.Marshal.Name, snapshot.Marshal.PositionX, snapshot.Marshal.PositionY, snapshot.Marshal.Health, snapshot.Marshal.MaxHealth, "ally", string.Equals(snapshot.Marshal.Name, snapshot.SelectedEntity, StringComparison.Ordinal), $"HP {snapshot.Marshal.Health:0}/{snapshot.Marshal.MaxHealth:0} | {snapshot.Marshal.Tags}"),
                new StageEvidenceActor(snapshot.Runner.Name, snapshot.Runner.PositionX, snapshot.Runner.PositionY, snapshot.Runner.Health, snapshot.Runner.MaxHealth, "support", string.Equals(snapshot.Runner.Name, snapshot.SelectedEntity, StringComparison.Ordinal), $"HP {snapshot.Runner.Health:0}/{snapshot.Runner.MaxHealth:0} | {snapshot.Runner.Tags}"),
                new StageEvidenceActor(snapshot.Caster.Name, snapshot.Caster.PositionX, snapshot.Caster.PositionY, snapshot.Caster.Health, snapshot.Caster.MaxHealth, "enemy", string.Equals(snapshot.Caster.Name, snapshot.SelectedEntity, StringComparison.Ordinal), $"HP {snapshot.Caster.Health:0}/{snapshot.Caster.MaxHealth:0} | {snapshot.Caster.Tags}")
            };

            return new StageEvidenceFrame(
                "Champion Control Showcase",
                snapshot.Step,
                snapshot.Note,
                snapshot.SelectedEntity,
                "Control Buffs",
                $"Headless evidence | frame {snapshot.FrameIndex:000} | overlay lines {snapshot.OverlayLines.Count}",
                summaryLines,
                detailLines,
                actors);
        }

        private static UiAcceptanceEvidenceFrame BuildControlTimelineFrame(ControlShowcaseSnapshot snapshot, string fileName)
        {
            return new UiAcceptanceEvidenceFrame(
                snapshot.Step,
                fileName,
                $"{snapshot.FrameIndex:000}",
                $"{snapshot.SelectedEntity} selected",
                snapshot.Note,
                $"marshal={snapshot.Marshal.Tags}",
                $"runner={snapshot.Runner.Tags}",
                $"caster={snapshot.Caster.Tags}",
                snapshot.OverlayLines.Take(6).ToArray());
        }

        private static void WriteSnapshotSvg(ControlShowcaseSnapshot snapshot, string path)
        {
            Vector2 marshalPoint = ToStagePoint(new Vector2(snapshot.Marshal.PositionX, snapshot.Marshal.PositionY));
            Vector2 runnerPoint = ToStagePoint(new Vector2(snapshot.Runner.PositionX, snapshot.Runner.PositionY));
            Vector2 casterPoint = ToStagePoint(new Vector2(snapshot.Caster.PositionX, snapshot.Caster.PositionY));

            string overlayLines = string.Join(
                Environment.NewLine,
                snapshot.OverlayLines.Take(12).Select((line, index) =>
                    $"""<text x="950" y="{188 + index * 22}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">{EscapeSvg(TrimForPaint(line, 74))}</text>"""));

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{ImageWidth}}" height="{{ImageHeight}}" viewBox="0 0 {{ImageWidth}} {{ImageHeight}}">
  <rect width="{{ImageWidth}}" height="{{ImageHeight}}" fill="#0b1018" />
  <text x="40" y="42" fill="#ffffff" font-size="30" font-family="Consolas, monospace">Champion Control Showcase | {{snapshot.FrameIndex:000}} {{EscapeSvg(snapshot.Step)}}</text>
  <text x="40" y="70" fill="#f4d074" font-size="18" font-family="Consolas, monospace">{{EscapeSvg(snapshot.Note)}}</text>
  <rect x="40" y="90" width="860" height="760" rx="18" fill="#152130" stroke="#5b7fa0" stroke-width="2" />
  <circle cx="{{marshalPoint.X:0.##}}" cy="{{marshalPoint.Y:0.##}}" r="18" fill="#62baff" />
  <text x="{{marshalPoint.X + 24:0.##}}" y="{{marshalPoint.Y + 6:0.##}}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">Marshal</text>
  <circle cx="{{runnerPoint.X:0.##}}" cy="{{runnerPoint.Y:0.##}}" r="18" fill="#76e88c" />
  <text x="{{runnerPoint.X + 24:0.##}}" y="{{runnerPoint.Y + 6:0.##}}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">Runner</text>
  <circle cx="{{casterPoint.X:0.##}}" cy="{{casterPoint.Y:0.##}}" r="18" fill="#ff926a" />
  <text x="{{casterPoint.X + 24:0.##}}" y="{{casterPoint.Y + 6:0.##}}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">Caster</text>
  {{RenderActorBlock(snapshot.SelectedEntity, snapshot.Marshal, snapshot.Runner, snapshot.Caster)}}
  <text x="950" y="166" fill="#ffffff" font-size="20" font-family="Consolas, monospace">Overlay</text>
  {{overlayLines}}
</svg>
""";

            File.WriteAllText(path, svg);
        }

        private static string RenderActorBlock(string selectedEntity, ActorSnapshot marshal, ActorSnapshot runner, ActorSnapshot caster)
        {
            return string.Join(
                Environment.NewLine,
                RenderActorLines(selectedEntity, marshal, 96),
                RenderActorLines(selectedEntity, runner, 246),
                RenderActorLines(selectedEntity, caster, 396));
        }

        private static IEnumerable<string> RenderActorLines(string selectedEntity, ActorSnapshot actor, int top)
        {
            yield return $"""<text x="950" y="{top}" fill="#ffffff" font-size="20" font-family="Consolas, monospace">{EscapeSvg(actor.Name)}{(string.Equals(actor.Name, selectedEntity, StringComparison.Ordinal) ? " [Selected]" : string.Empty)}</text>""";
            yield return $"""<text x="950" y="{top + 26}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">HP {actor.Health:0} | Pos ({actor.PositionX:0}, {actor.PositionY:0})</text>""";
            yield return $"""<text x="950" y="{top + 48}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">MoveSpeed {actor.MoveSpeedCurrent:0.#}/{actor.MoveSpeedBase:0.#} | moveBlocked={actor.MoveBlocked} | actionBlocked={actor.ActionBlocked}</text>""";
            yield return $"""<text x="950" y="{top + 70}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">Tags {EscapeSvg(TrimForPaint(actor.Tags, 70))}</text>""";
            yield return $"""<text x="950" y="{top + 92}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">Exec {(actor.HasExec ? EscapeSvg(actor.ExecState) : "Idle")}</text>""";
        }

        private static void WriteTimelineSvg(IReadOnlyList<CaptureFrame> frames, string path)
        {
            if (frames.Count == 0)
            {
                return;
            }

            string lines = string.Join(
                Environment.NewLine,
                frames.Select((frame, index) =>
                    $"""<text x="40" y="{100 + index * 36}" fill="#e0e8f0" font-size="22" font-family="Consolas, monospace">{frame.FrameIndex:000} | {EscapeSvg(frame.Step)} | {EscapeSvg(frame.FileName)}</text>"""));

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="{{Math.Max(240, 140 + frames.Count * 36)}}" viewBox="0 0 1600 {{Math.Max(240, 140 + frames.Count * 36)}}">
  <rect width="1600" height="{{Math.Max(240, 140 + frames.Count * 36)}}" fill="#081018" />
  <text x="40" y="56" fill="#ffffff" font-size="30" font-family="Consolas, monospace">Champion control showcase evidence timeline</text>
  {{lines}}
</svg>
""";

            File.WriteAllText(path, svg);
        }

        private static string BuildTraceJsonl(IReadOnlyList<ControlShowcaseSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                ControlShowcaseSnapshot snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"champion-control-{i + 1:000}",
                    frame = snapshot.FrameIndex,
                    step = snapshot.Step,
                    note = snapshot.Note,
                    selected_entity = snapshot.SelectedEntity,
                    marshal = snapshot.Marshal,
                    runner = snapshot.Runner,
                    caster = snapshot.Caster,
                    overlay = snapshot.OverlayLines
                }));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<ControlShowcaseSnapshot> snapshots, IReadOnlyList<double> frameTimesMs, float baselineRunnerTravel, float slowRunnerTravel, float runnerRecoverTravel)
        {
            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            ControlShowcaseSnapshot final = snapshots[^1];

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: champion-control-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: inspect and play a reusable control-buff showcase with visible slow, silence, root, and stun behavior.");
            sb.AppendLine("- Gameplay domain: real `ChampionSkillSandboxMod` map runtime plus reusable `CommonControlBuffsMod` effect/tag/sink infrastructure.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Maps/champion_control_showcase.json`");
            sb.AppendLine("- Clock profile: fixed `1/60s`");
            sb.AppendLine("- Initial entities: `Control Marshal`, `Control Runner`, `Control Caster`");
            sb.AppendLine("- Evidence images: `artifacts/acceptance/champion-control-showcase/screens/*.png`, `artifacts/acceptance/champion-control-showcase/screens/timeline.png`");
            sb.AppendLine("- Evidence note: PNGs are headless runtime evidence cards rendered from acceptance snapshots.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Load the playable control showcase map and verify the control state and marshal loadout.");
            sb.AppendLine("2. Drive the runner through the real move-order path, then fire the marshal's Q/E/R control skills through cast orders.");
            sb.AppendLine("3. Submit a real hostile cast from the caster, then fire the marshal's W/R control skills to prove startup rejection and active-cast interrupt.");
            sb.AppendLine("4. Write trace, path, battle report, and evidence frames for human review.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            foreach (string entry in timeline)
            {
                sb.AppendLine($"- {entry}");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- result: success");
            sb.AppendLine($"- runner_baseline_travel_cm: {baselineRunnerTravel:0.#}");
            sb.AppendLine($"- runner_slowed_travel_cm: {slowRunnerTravel:0.#}");
            sb.AppendLine($"- runner_recovery_travel_cm: {runnerRecoverTravel:0.#}");
            sb.AppendLine($"- final_selected_entity: {final.SelectedEntity}");
            sb.AppendLine($"- final_runner_tags: {final.Runner.Tags}");
            sb.AppendLine($"- final_caster_tags: {final.Caster.Tags}");
            sb.AppendLine($"- final_caster_exec: {(final.Caster.HasExec ? final.Caster.ExecState : "Idle")}");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- total_actions: {timeline.Count}");
            sb.AppendLine($"- evidence_captures: {snapshots.Count}");
            sb.AppendLine("- reusable_effects_proven: slow, silence, root, stun");
            sb.AppendLine("- sink_projection_proven: move-block and action-block");
            sb.AppendLine("- cast_gate_reuse_proven: silence startup rejection, stun interrupt");
            sb.AppendLine($"- median_tick_ms: {medianTickMs:0.###}");
            sb.AppendLine($"- max_tick_ms: {maxTickMs:0.###}");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load champion_control_showcase] --> B[Submit runner move order]",
                "    B --> C[Apply Slow -> MoveSpeed drops but runner still moves]",
                "    C --> D[Apply Root -> move blocked and nav speed becomes 0]",
                "    D --> E[Root expires -> runner movement recovers]",
                "    E --> F[Apply Stun on runner -> ActionBlocked and move blocked]",
                "    F --> G[Submit baseline Arc Pulse cast -> marshal takes damage]",
                "    G --> H[Apply Silence on caster -> manual cast blocked before exec]",
                "    H --> I{Exec prevented and cooldown unchanged?}",
                "    I -->|no| X[Fail: silence wiring broken]",
                "    I -->|yes| J[Submit cast again and wait for active exec]",
                "    J --> K[Apply Stun mid-cast -> exec interrupted]",
                "    K --> L{Marshal HP unchanged after stun?}",
                "    L -->|no| Y[Fail: stun did not interrupt active cast]",
                "    L -->|yes| M[Write trace, battle report, path, PNG timeline]"
            }) + Environment.NewLine;
        }

        private static string TrimForPaint(string value, int maxChars)
        {
            return string.IsNullOrEmpty(value) || value.Length <= maxChars ? value : value[..Math.Max(0, maxChars - 3)] + "...";
        }

        private static Vector2 ToStagePoint(Vector2 world)
        {
            float x = 40f + (world.X - WorldMinX) / (WorldMaxX - WorldMinX) * 870f;
            float y = 840f - (world.Y - WorldMinY) / (WorldMaxY - WorldMinY) * 760f;
            return new Vector2(x, y);
        }

        private static string EscapeSvg(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double[] copy = values.ToArray();
            Array.Sort(copy);
            int mid = copy.Length / 2;
            return (copy.Length & 1) != 0 ? copy[mid] : (copy[mid - 1] + copy[mid]) * 0.5d;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string srcDir = Path.Combine(dir.FullName, "src");
                string assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed record ControlShowcaseSnapshot(int FrameIndex, string Step, string Note, string SelectedEntity, IReadOnlyList<string> OverlayLines, ActorSnapshot Marshal, ActorSnapshot Runner, ActorSnapshot Caster);
        private sealed record ActorSnapshot(string Name, float PositionX, float PositionY, float Health, float MaxHealth, float MoveSpeedCurrent, float MoveSpeedBase, string Tags, bool MoveBlocked, bool ActionBlocked, bool HasExec, string ExecState);
        private sealed record CaptureFrame(int FrameIndex, string Step, string FileName);

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
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
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
