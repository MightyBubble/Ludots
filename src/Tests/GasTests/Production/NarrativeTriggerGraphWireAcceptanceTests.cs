using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Tests.TestCommon;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// Narrative showcase wiring: map TriggerGraph parks on AwaitCallback(DialogConfirm)
    /// via InlineGraph macro; Dialogue Completer resumes Continuation and writes MapVar;
    /// Sequencer Signal fires Halt-only actionGraphId into MapVar. Not a playable gallery
    /// substitute — proves the story↔graph contract on the real narrative map.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class NarrativeTriggerGraphWireAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string TestInputBackendKey = "Tests.NarrativeWire.InputBackend";
        private const string HeadlessCameraKey = "Tests.NarrativeWire.HeadlessCamera";
        private const string MapId = "narrative_showcase_hub";

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

        [Test]
        public void NarrativeMap_AwaitDialogConfirm_ContinuesAfterChoice_WritesAck()
        {
            var frameTimesMs = new List<double>();
            using GameEngine engine = CreateEngine();
            GraphCallbackService callbacks = engine.GetService(CoreServiceKeys.GraphCallbackService)
                ?? throw new InvalidOperationException("GraphCallbackService missing.");
            DialogueRuntime dialogue = engine.GetService(CoreServiceKeys.DialogueRuntime)
                ?? throw new InvalidOperationException("DialogueRuntime missing.");

            engine.LoadMap(MapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                () => string.Join(" | ", engine.TriggerManager.Errors));

            MapVariableStore variables = engine.CurrentMapSession!.Variables
                ?? throw new InvalidOperationException("Variables missing.");
            Assert.That(variables.ReadInt("dialog_await_ack"), Is.EqualTo(0));

            TickUntil(engine, frameTimesMs,
                () => callbacks.TryGetOldestLiveHandleByCallbackType(GraphCallbackTypes.DialogConfirm, out _),
                maxFrames: 120,
                "MapHeartbeat once must park AwaitCallback(DialogConfirm) via InlineGraph wire.");

            dialogue.StartDialogue("Dialogue.Narrative.Briefing");
            Assert.That(dialogue.HasActiveDialogue, Is.True);
            dialogue.ChooseOption(0);

            TickUntil(engine, frameTimesMs,
                () => variables.ReadInt("dialog_await_ack") == 1,
                maxFrames: 60,
                "Continuation must resume after Dialogue Completer and write dialog_await_ack.");
            Assert.That(
                callbacks.TryGetOldestLiveHandleByCallbackType(GraphCallbackTypes.DialogConfirm, out _),
                Is.False);
        }

        [Test]
        public void NarrativeSequence_Signal_WritesSignalAckMapVar()
        {
            var frameTimesMs = new List<double>();
            using GameEngine engine = CreateEngine();
            SequencerRuntime sequencer = engine.GetService(CoreServiceKeys.SequencerRuntime)
                ?? throw new InvalidOperationException("SequencerRuntime missing.");

            engine.LoadMap(MapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            MapVariableStore variables = engine.CurrentMapSession!.Variables
                ?? throw new InvalidOperationException("Variables missing.");
            Assert.That(variables.ReadInt("signal_ack"), Is.EqualTo(0));

            sequencer.Start("Sequence.Narrative.TrialReveal");
            Assert.That(sequencer.HasActiveSequence, Is.True);

            TickUntil(engine, frameTimesMs,
                () => variables.ReadInt("signal_ack") == 1,
                maxFrames: 180,
                "Sequencer Signal must ExecuteAction(Graph.Narrative.Action.SignalAck) once.");
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
            var cameraAdapter = new StubCameraAdapter();
            PresentationTimingDiagnostics? timingDiagnostics =
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.AuthorityCamera(), view);
            var screenRayProvider = new CoreScreenRayProvider(engine.AuthorityCamera(), view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);
            var culling = new CameraCullingSystem(
                engine.World, engine.AuthorityCamera(), engine.SpatialQueries, view,
                cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext[HeadlessCameraKey] = cameraPresenter;

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
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> predicate,
            int maxFrames,
            string because)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                long t0 = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                if (engine.GlobalContext[HeadlessCameraKey] is CameraPresenter presenter)
                {
                    presenter.Update(engine.AuthorityCamera(), interpolationAlpha: 1f);
                }

                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }

            Assert.Fail($"{because} (frames={maxFrames})");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private System.Numerics.Vector2 _mousePosition;
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out var isDown) && isDown;
            public System.Numerics.Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height) =>
                Resolution = new System.Numerics.Vector2(width, height);
            public System.Numerics.Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
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
