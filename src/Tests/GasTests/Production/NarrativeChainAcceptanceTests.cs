using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests.TestCommon;
using Ludots.UI;
using NarrativeChainShowcaseMod;
using NarrativeFrontendMod;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// Headless end-to-end acceptance for the narrative chain showcase:
    /// dialogue -> cinematic (subtitle track + presenter impulse commands + camera step with
    /// clear-on-complete) -> trigger -> forced activity on the UiRegions HUD modal (F/G keys) ->
    /// task.create -> task completion -> next_task_id auto-advance -> debrief on_enter dialogue
    /// -> verdict branches (map-variable write vs event broadcast), with every cross-domain
    /// effect routed through the trigger pipeline or declarative task wiring.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class NarrativeChainAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string TestInputBackendKey = "Tests.NarrativeChain.InputBackend";
        private const string ArtifactDirName = "narrative-chain";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "NarrativeFrontendMod",
            "UiRegionsMod",
            "NarrativeChainShowcaseMod",
        };

        private readonly List<string> _timeline = new();

        [Test]
        public void ChainHappyPath_SealBranch_WritesMapVariableViaTrigger()
        {
            using GameEngine engine = CreateEngine();
            var backend = GetInputBackend(engine);
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            Assert.That(ActiveCameraId(engine), Is.EqualTo(NarrativeChainIds.HubDefaultCameraId));
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "woke on its own"), 30);
            Record("dialogue", "opening dialogue visible with choice list");

            PressButton(engine, backend, "<Keyboard>/1");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            Record("dialogue", "opening choice committed; end node auto-advanced");

            TickUntil(engine, () => director.HasActiveCinematic && UiContains(uiRoot, "First lamp"), 30);
            Record("cinematic", "cinematic started; first subtitle on the presenter chain");

            TickUntil(engine, () => UiContains(uiRoot, "Second lamp"), 60);
            TickUntil(engine, () => string.Equals(ActiveCameraId(engine), NarrativeChainIds.RevealStepCameraId, StringComparison.Ordinal), 30);
            Assert.That(GetClientCamera(engine).IsVirtualCameraActive(NarrativeChainIds.RevealStepCameraId), Is.True);
            Record("cinematic", "reveal_2 camera step switched the active virtual camera to Tactical");

            TickUntil(engine, () => !director.HasActiveCinematic, 300);
            var presenterEvents = GetChainRuntime(engine).Events
                .Where(e => e.EventName == "presenter_command").ToList();
            Assert.That(presenterEvents.Count, Is.EqualTo(3));
            Assert.That(presenterEvents[0].Detail, Does.Contain("First lamp"));
            Assert.That(presenterEvents[1].Detail, Does.Contain("Second lamp"));
            Assert.That(presenterEvents[2].Detail, Does.Contain("Third lamp"));
            Record("cinematic", "all three subtitle steps presented with presenter commands");

            TickUntil(engine, () =>
                !GetClientCamera(engine).IsVirtualCameraActive(NarrativeChainIds.RevealStepCameraId) &&
                string.Equals(ActiveCameraId(engine), NarrativeChainIds.HubDefaultCameraId, StringComparison.Ordinal), 30);
            Record("cinematic", "clearCameraOnComplete cleared the cinematic camera; hub default camera restored");

            TickUntil(engine, () =>
                FindActiveActivity(engine, NarrativeChainIds.DecideActivityId) != null &&
                UiContains(uiRoot, "Dispatch the survey crew") &&
                UiContains(uiRoot, "Hold the watch"), 60);
            Record("activity", "forced decision activity offered; HUD activity modal shows both options");

            PressButton(engine, backend, "<Keyboard>/f");
            TickUntil(engine, () => TaskStateOf(engine, NarrativeChainIds.SurveyTaskId) == TaskInstanceState.Active, 30);
            TickUntil(engine, () => UiContains(uiRoot, "Relay Survey"), 30);
            Record("task", "F-key confirm resolved the activity; task.create activated the survey task on the HUD list");

            TickUntil(engine, () => TaskStateOf(engine, NarrativeChainIds.SurveyTaskId) == TaskInstanceState.Completed, 120);
            Record("task", "objective signal completed the survey task");

            TickUntil(engine, () => TaskStateOf(engine, NarrativeChainIds.DebriefTaskId) == TaskInstanceState.Active, 30);
            Record("task", "next_task_id auto-advanced the chain into the debrief task");

            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "crew is back"), 60);
            Assert.That(director.TryGetActiveDialogueView(out NarrativeDialogueView verdictView), Is.True);
            Assert.That(verdictView.DialogueId, Is.EqualTo(NarrativeChainIds.VerdictDialogueId));
            Record("dialogue", "debrief on_enter_dialogue_id opened the verdict dialogue");

            PressButton(engine, backend, "<Keyboard>/1");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);

            var runtime = GetChainRuntime(engine);
            Assert.That(director.GetVariable(NarrativeChainIds.NarrativeVariableLore).IntValue, Is.EqualTo(1));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeChainIds.MapVariableAlarms), Is.EqualTo(1));
            Assert.That(runtime.PresenterCommandCount, Is.EqualTo(3));
            Assert.That(runtime.HeraldEventCount, Is.EqualTo(0));
            Assert.That(runtime.ChainFinished, Is.True);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("verdict", "seal branch: narrative variable +1 and map variable written via trigger signal");

            WriteArtifacts("happy_seal");
        }

        [Test]
        public void ChainHeraldBranch_BroadcastsEventWithoutMapWrite()
        {
            using GameEngine engine = CreateEngine();
            var backend = GetInputBackend(engine);
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => director.HasActiveDialogue, 30);
            Record("dialogue", "opening dialogue visible");
            PressButton(engine, backend, "<Keyboard>/1");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            TickUntil(engine, () => director.HasActiveCinematic, 30);
            Record("cinematic", $"presenter commands={GetChainRuntime(engine).PresenterCommandCount}");
            TickUntil(engine, () => !director.HasActiveCinematic, 300);
            TickUntil(engine, () =>
                FindActiveActivity(engine, NarrativeChainIds.DecideActivityId) != null &&
                UiContains(uiRoot, "Dispatch the survey crew"), 60);
            Record("activity", "forced decision activity offered on the HUD modal");
            PressButton(engine, backend, "<Keyboard>/f");
            Record("activity", "confirmed dispatch via [F]");
            TickUntil(engine, () => TaskStateOf(engine, NarrativeChainIds.SurveyTaskId) == TaskInstanceState.Active, 30);
            Record("task", "survey task created by the confirmed option");
            TickUntil(engine, () => TaskStateOf(engine, NarrativeChainIds.SurveyTaskId) == TaskInstanceState.Completed, 120);
            Record("task", "survey completed; crew returned with the third lamp's reading");
            TickUntil(engine, () => TaskStateOf(engine, NarrativeChainIds.DebriefTaskId) == TaskInstanceState.Active, 30);
            Record("task", "debrief task auto-started by the task chain");
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "crew is back"), 60);

            PressButton(engine, backend, "<Keyboard>/2");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);

            var runtime = GetChainRuntime(engine);
            Assert.That(runtime.HeraldEventCount, Is.EqualTo(1));
            Assert.That(director.GetVariable(NarrativeChainIds.NarrativeVariableLore).IntValue, Is.EqualTo(0));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeChainIds.MapVariableAlarms), Is.EqualTo(0));
            Assert.That(runtime.ChainFinished, Is.True);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("verdict", "herald branch: event broadcast consumed by presenter impulse; no map write");

            WriteArtifacts("herald_branch");
        }

        [Test]
        public void ChainGuard_DeclineOption_LeavesChainIdle()
        {
            using GameEngine engine = CreateEngine();
            var backend = GetInputBackend(engine);
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => director.HasActiveDialogue, 30);
            Record("dialogue", "opening dialogue visible");
            PressButton(engine, backend, "<Keyboard>/1");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            TickUntil(engine, () => director.HasActiveCinematic, 30);
            Record("cinematic", $"presenter commands={GetChainRuntime(engine).PresenterCommandCount}");
            TickUntil(engine, () => !director.HasActiveCinematic, 300);
            TickUntil(engine, () =>
                FindActiveActivity(engine, NarrativeChainIds.DecideActivityId) != null &&
                UiContains(uiRoot, "Hold the watch"), 60);
            Record("activity", "forced decision activity offered on the HUD modal");

            PressButton(engine, backend, "<Keyboard>/g");
            Tick(engine, 60);

            Assert.That(TaskStateOf(engine, NarrativeChainIds.SurveyTaskId), Is.Null);
            Assert.That(TaskStateOf(engine, NarrativeChainIds.DebriefTaskId), Is.Null);
            Assert.That(director.HasActiveDialogue, Is.False);
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeChainIds.MapVariableAlarms), Is.EqualTo(0));
            Assert.That(GetChainRuntime(engine).ChainFinished, Is.False);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("guard", "decline baseline option: no task, no debrief, no verdict dialogue, chain stays idle");

            WriteArtifacts("guard_decline");
        }

        private static NarrativeDirector GetDirector(GameEngine engine) =>
            engine.GetService(CoreServiceKeys.NarrativeDirector)
                ?? throw new InvalidOperationException("NarrativeDirector was not installed.");

        private static UIRoot GetUiRoot(GameEngine engine) =>
            engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot was not installed.");

        private static NarrativeChainShowcaseMod.Runtime.NarrativeChainRuntime GetChainRuntime(GameEngine engine) =>
            engine.GlobalContext["NarrativeChain.Runtime"] as NarrativeChainShowcaseMod.Runtime.NarrativeChainRuntime
                ?? throw new InvalidOperationException("NarrativeChain runtime was not installed.");

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

        private static ActivityView? FindActiveActivity(GameEngine engine, string activityId)
        {
            if (engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
            {
                return null;
            }

            foreach (ActivityView view in activities.CaptureViews())
            {
                if (view.State == ActivityInstanceState.Active &&
                    string.Equals(view.ActivityId, activityId, StringComparison.Ordinal))
                {
                    return view;
                }
            }

            return null;
        }

        private static CameraManager GetClientCamera(GameEngine engine)
        {
            var views = engine.GetService(CoreServiceKeys.LogicViewRegistry) as LogicViewRegistry
                ?? throw new InvalidOperationException("LogicViewRegistry was not installed.");
            if (!views.TryGetClientPresentCamera(out CameraManager camera))
            {
                throw new InvalidOperationException("Client present camera was not registered.");
            }

            return camera;
        }

        private static string ActiveCameraId(GameEngine engine) =>
            GetClientCamera(engine).VirtualCameraBrain?.ActiveCameraId ?? string.Empty;

        private static void AssertUiContains(UIRoot uiRoot, string needle) =>
            Assert.That(UiContains(uiRoot, needle), Is.True, $"UI text should contain '{needle}'.");

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
            engine.LoadMap(NarrativeChainIds.MapId);
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
            report.AppendLine("# Narrative Chain Acceptance — MUD Battle Report");
            report.AppendLine();
            report.AppendLine($"- scenario: {scenario}");
            report.AppendLine("- build: headless GameEngine + trigger pipeline");
            report.AppendLine($"- map: {NarrativeChainIds.MapId} (seed: fixed content, no rng)");
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
            report.AppendLine("- PASS: full chain completed for this scenario branch.");
            report.AppendLine();
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), report.ToString());

            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), PathMermaid);
        }

        private const string PathMermaid = """
            flowchart TD
                start([map loaded, hub camera Inspect]) --> dlg[opening dialogue with choices]
                dlg -->|choice 1/2| openEnd[open_end node: EmitSignal chain.opened]
                openEnd -->|dialogue closed, advance system| cine[Cinematic.Chain.Reveal]
                cine -->|per step: CinematicStepEntered| impulse[presenter command: camera impulse + subtitle]
                impulse --> cine
                cine -->|reveal_2 camera step| tactical[active camera -> Camera.Profile.Tactical]
                tactical --> cine
                cine -->|CinematicCompleted + clearCameraOnComplete| camClear[cinematic camera cleared, hub camera restored]
                camClear --> act{{forced activity: activity.chain.decide on HUD modal}}
                act -->|F key: option confirm, task.create effect| task[Task.Chain.Survey active]
                act -->|G key: option decline baseline| idle[chain idle: no task, no verdict]
                task -->|delayed objective signal| taskDone[task completed]
                taskDone -->|next_task_id auto-advance| debrief[Task.Chain.Debrief active]
                debrief -->|on_enter_dialogue_id| verdict[verdict dialogue]
                verdict -->|choice 1 seal: AddVariable + EmitSignal cmd| mapVar[map variable chain_alarms +1 via trigger]
                verdict -->|choice 2 herald: EmitSignal event| event[herald event broadcast -> camera impulse]
                mapVar --> done([chain.finished])
                event --> done
                idle -.guard branch.-> done
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
