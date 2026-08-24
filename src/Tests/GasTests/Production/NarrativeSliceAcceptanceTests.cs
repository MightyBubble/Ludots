using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Tests.TestCommon;
using Ludots.UI;
using NarrativeSlicesMod;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// Headless slice acceptance for the narrative slices showcase. Each slice is one
    /// self-contained scenario: dialogue_gate proves choice condition gating on narrative
    /// variables; action_gallery drives eight narrative actions (SetVariable, AddVariable,
    /// StartTask, CompleteTask, FailTask, ActivateCamera, ClearCamera, EmitSignal) and
    /// asserts their observable end state on variables, task instances and the authority
    /// virtual camera brain.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class NarrativeSliceAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string TestInputBackendKey = "Tests.NarrativeSlices.InputBackend";
        private const string ArtifactDirName = "narrative-slices";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "NarrativeFrontendMod",
            "NarrativeSlicesMod",
        };

        private readonly List<string> _timeline = new();

        static NarrativeSliceAcceptanceTests()
        {
            // The ModLoader adopts already-loaded default-context assemblies for mods whose
            // simple name matches (TryResolvePreloadedAssembly). Touching the frontend mod's
            // test-bin copy before engine construction makes the loader adopt that single copy
            // for NarrativeFrontendMod, so this fixture's assembly identity matches the
            // NarrativeFrontendService instance the mod registers; without it the loader loads
            // the repo-bin copy into its ModLoadContext and the typed service lookup splits
            // across two assemblies, leaving the slices panel publisher without a service.
            _ = typeof(NarrativeFrontendMod.NarrativeFrontendServiceKeys).Assembly;
        }

        [Test]
        public void GateSlice_LockedChoiceHiddenUntilVariableConditionMet()
        {
            using GameEngine engine = CreateEngine();
            var backend = GetInputBackend(engine);
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            StartSlice(engine, NarrativeSlicesIds.SliceDialogueGate);
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "gallery ledger"), 30);
            Record("dialogue", "gate dialogue visible on the presenter chain");

            var choiceIds = CurrentChoiceIds(director);
            Assert.That(choiceIds, Is.EquivalentTo(new[] { NarrativeSlicesIds.ChoiceOpenYes }));
            Record("dialogue", "locked choice hidden: choices=[open_yes] while gallery_lore=0");

            PressButton(engine, backend, "<Keyboard>/1");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            Assert.That(director.GetVariable(NarrativeSlicesIds.GalleryLoreVariableId).IntValue, Is.EqualTo(1));
            Record("dialogue", "grant node committed: gallery_lore=1, signal slice.gate.granted emitted");

            StartSlice(engine, NarrativeSlicesIds.SliceDialogueGate);
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "sealed margin"), 30);
            choiceIds = CurrentChoiceIds(director);
            Assert.That(choiceIds, Is.EquivalentTo(new[]
            {
                NarrativeSlicesIds.ChoiceOpenYes,
                NarrativeSlicesIds.ChoiceOpenLocked,
            }));
            Record("dialogue", "locked choice revealed: gallery_lore>=1 satisfied");

            PressButton(engine, backend, "<Keyboard>/2");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            Record("dialogue", "locked choice walked to the seal node; signal slice.gate.finished emitted");

            var runtime = GetSlicesRuntime(engine);
            Assert.That(director.GetVariable(NarrativeSlicesIds.GalleryLoreVariableId).IntValue, Is.EqualTo(1));
            var signalTrace = runtime.Events
                .Where(e => e.EventName == "signal")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(signalTrace.IndexOf(NarrativeSlicesIds.SignalGateGranted), Is.GreaterThanOrEqualTo(0));
            Assert.That(signalTrace.IndexOf(NarrativeSlicesIds.SignalGateFinished), Is.GreaterThan(
                signalTrace.IndexOf(NarrativeSlicesIds.SignalGateGranted)));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(2));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("gate", "both signals received in order; slice_counter=2; lore not double-counted");

            WriteArtifacts(NarrativeSlicesIds.SliceDialogueGate);
        }

        [Test]
        public void GallerySlice_EightNarrativeActions_LeaveObservableState()
        {
            using GameEngine engine = CreateEngine();
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            StartSlice(engine, NarrativeSlicesIds.SliceActionGallery);
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "Set the counter"), 30);
            Record("dialogue", "gallery dialogue started; nine action nodes chained by auto-advance");

            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.GalleryInspectCameraId, 120);
            Record("camera", $"ActivateCamera action observed live: brain active id='{NarrativeSlicesIds.GalleryInspectCameraId}'");

            TickUntil(engine, () => !director.HasActiveDialogue, 300);
            Record("dialogue", "gallery sequence finished");

            Assert.That(ActiveCameraId(engine), Is.EqualTo(NarrativeSlicesIds.MapDefaultCameraId));
            Record("camera", $"ClearCamera action observed live: brain back to '{NarrativeSlicesIds.MapDefaultCameraId}'");

            Assert.That(director.GetVariable(NarrativeSlicesIds.GallerySliceVariableId).IntValue, Is.EqualTo(8));
            Assert.That(TaskStateOf(engine, NarrativeSlicesIds.GalleryAlphaTaskId), Is.EqualTo(TaskInstanceState.Completed));
            Assert.That(TaskStateOf(engine, NarrativeSlicesIds.GalleryBetaTaskId), Is.EqualTo(TaskInstanceState.Failed));
            Record("actions", "slice_var=8 (Set 7 + Add 1); Alpha=Completed; Beta=Failed");

            var runtime = GetSlicesRuntime(engine);
            var cameraRequests = runtime.Events
                .Where(e => e.EventName == "virtual_camera_request")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(cameraRequests.Any(d => d.Contains(NarrativeSlicesIds.GalleryInspectCameraId, StringComparison.Ordinal)), Is.True);
            Assert.That(cameraRequests.Any(d => d.Contains("clear", StringComparison.Ordinal)), Is.True);

            var taskTrace = runtime.Events
                .Where(e => e.Phase == "task")
                .Select(e => $"{e.EventName}:{e.Detail}")
                .ToList();
            Assert.That(taskTrace, Does.Contain($"activated:{NarrativeSlicesIds.GalleryAlphaTaskId}"));
            Assert.That(taskTrace, Does.Contain($"completed:{NarrativeSlicesIds.GalleryAlphaTaskId}"));
            Assert.That(taskTrace, Does.Contain($"activated:{NarrativeSlicesIds.GalleryBetaTaskId}"));
            Assert.That(taskTrace, Does.Contain($"failed:{NarrativeSlicesIds.GalleryBetaTaskId}"));

            var signalTrace = runtime.Events
                .Where(e => e.EventName == "signal")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(signalTrace, Does.Contain(NarrativeSlicesIds.SignalGalleryDone));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(1));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("gallery", "camera requests, task lifecycle and slice.gallery.done all traced; slice_counter=1");

            WriteArtifacts(NarrativeSlicesIds.SliceActionGallery);
        }

        private static NarrativeDirector GetDirector(GameEngine engine) =>
            engine.GetService(CoreServiceKeys.NarrativeDirector)
                ?? throw new InvalidOperationException("NarrativeDirector was not installed.");

        private static UIRoot GetUiRoot(GameEngine engine) =>
            engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot was not installed.");

        private static NarrativeSlicesMod.Runtime.NarrativeSlicesRuntime GetSlicesRuntime(GameEngine engine) =>
            engine.GlobalContext["NarrativeSlices.Runtime"] as NarrativeSlicesMod.Runtime.NarrativeSlicesRuntime
                ?? throw new InvalidOperationException("NarrativeSlices runtime was not installed.");

        private static void StartSlice(GameEngine engine, string sliceId) =>
            GetSlicesRuntime(engine).StartSlice(sliceId);

        private static string ActiveCameraId(GameEngine engine) =>
            engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId ?? string.Empty;

        private static List<string> CurrentChoiceIds(NarrativeDirector director) =>
            director.GetCurrentChoices().Select(choice => choice.Id).ToList();

        private void Record(string phase, string message) => _timeline.Add($"[T+{_timeline.Count + 1:D3}] [{phase}] {message}");

        private static TaskInstanceState? TaskStateOf(GameEngine engine, string taskId)
        {
            if (engine.GetService(CoreServiceKeys.TaskRuntimeService) is not TaskRuntimeService tasks)
            {
                return null;
            }

            foreach (TaskView view in tasks.CaptureViews())
            {
                if (string.Equals(view.TaskId, taskId, StringComparison.Ordinal))
                {
                    return view.State;
                }
            }

            return null;
        }

        private static bool UiContains(UIRoot uiRoot, string needle) =>
            AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(needle, StringComparison.Ordinal));

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            AcceptanceUiHostInstaller.Install(engine);
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

        private static TestInputBackend GetInputBackend(GameEngine engine) =>
            engine.GlobalContext[TestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Missing input backend.");

        private static void LoadMap(GameEngine engine)
        {
            engine.LoadMap(NarrativeSlicesIds.MapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Tick(engine, 8);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
        {
            backend.SetButton(path, true);
            Tick(engine, 2);
            backend.SetButton(path, false);
            Tick(engine, 2);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }

        private void WriteArtifacts(string scenario)
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactDirName, scenario);
            string screensDir = Path.Combine(artifactDir, "screens");
            AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

            File.WriteAllLines(
                Path.Combine(artifactDir, "trace.jsonl"),
                _timeline.Select((line, index) => JsonSerializer.Serialize(new
                {
                    seq = index + 1,
                    scenario,
                    at = line,
                })));

            var report = new StringBuilder();
            report.AppendLine("# Narrative Slices Acceptance — MUD Battle Report");
            report.AppendLine();
            report.AppendLine($"- scenario: {scenario}");
            report.AppendLine("- build: headless GameEngine + trigger pipeline");
            report.AppendLine($"- map: {NarrativeSlicesIds.MapId} (seed: fixed content, no rng)");
            report.AppendLine($"- clock: fixed {DeltaTime:0.0000}s per tick");
            report.AppendLine($"- executed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();
            report.AppendLine("## Timeline");
            report.AppendLine();
            foreach (string line in _timeline)
            {
                report.AppendLine($"- {line}");
            }
            report.AppendLine();
            report.AppendLine("## Outcome");
            report.AppendLine();
            report.AppendLine($"- PASS: slice '{scenario}' completed with all anchors observed.");
            report.AppendLine();
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), report.ToString());

            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), PathMermaidFor(scenario));
        }

        private static string PathMermaidFor(string scenario) => scenario switch
        {
            NarrativeSlicesIds.SliceDialogueGate => GatePathMermaid,
            NarrativeSlicesIds.SliceActionGallery => GalleryPathMermaid,
            _ => throw new InvalidOperationException($"Unknown slice scenario '{scenario}'."),
        };

        private const string GatePathMermaid = """
            flowchart TD
                start([StartSlice dialogue_gate]) --> root[gate_root choices]
                root -->|"gallery_lore = 0"| yes[open_yes visible]
                root -.->|"guard: gallery_lore >= 1 fails"| lockedHidden[open_locked hidden by condition]
                yes --> grant[node_grant: AddVariable gallery_lore+1, EmitSignal slice.gate.granted]
                grant --> end1([dialogue closed])
                end1 --> restart([StartSlice dialogue_gate again])
                restart --> root2[gate_root choices]
                root2 -->|"gallery_lore = 1"| both[open_yes + open_locked both visible]
                both --> pick[choice 2: open_locked]
                pick --> seal[node_seal: EmitSignal slice.gate.finished]
                seal --> done([slice finished: lore=1, counter=2])
            """;

        private const string GalleryPathMermaid = """
            flowchart TD
                start([StartSlice action_gallery]) --> setvar[gallery_setvar: SetVariable slice_var=7]
                setvar --> addvar[gallery_addvar: AddVariable +1]
                addvar --> alphaOn[gallery_start_alpha: StartTask Alpha]
                alphaOn --> alphaDone[gallery_complete_alpha: CompleteTask Alpha]
                alphaDone --> betaOn[gallery_start_beta: StartTask Beta]
                betaOn --> betaFail[gallery_fail_beta: FailTask Beta]
                betaFail --> camOn[gallery_camera_on: ActivateCamera Camera.Profile.Inspect]
                camOn -->|brain resolves Inspect| camClear[gallery_camera_clear: ClearCamera]
                camClear -->|brain falls back to map default Tactical| done[gallery_done: EmitSignal slice.gallery.done]
                done --> finish([slice finished: slice_var=8, Alpha=Completed, Beta=Failed])
                alphaOn -.->|task lifecycle events| trace[trigger trace: activated/completed/failed]
                betaOn -.-> trace
            """;

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
    }
}
