using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Providers;
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
    /// virtual camera brain; task_rules proves the any completion rule; task_chain proves
    /// next_task_id plus the on_enter_cinematic_id declared link; activity_execute_condition
    /// proves the option executability contract and documents the missing condition
    /// provider registration path as an engine gap; subtitle_presenter proves the per-step
    /// subtitle replacement sequence; presenter_track proves the step-boundary presenter
    /// command track with camera switching; map_variable_write proves map variable
    /// read/write as a parity decision input.
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
            // The slices mod itself needs the same preload: test methods only reference its
            // const strings (inlined at compile time), so without an explicit touch the first
            // engine of the process loads the repo-bin copy and the GlobalContext runtime
            // lookup splits across two NarrativeSlicesRuntime identities.
            _ = typeof(NarrativeFrontendMod.NarrativeFrontendServiceKeys).Assembly;
            _ = typeof(NarrativeSlicesIds).Assembly;
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
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "Welcome to the gallery"), 30);
            Record("dialogue", "gate dialogue visible on the presenter chain");

            var choiceIds = CurrentChoiceIds(director);
            Assert.That(choiceIds, Is.EquivalentTo(new[] { NarrativeSlicesIds.ChoiceOpenYes }));
            Record("dialogue", "locked choice hidden: choices=[open_yes] while gallery_lore=0");

            PressButton(engine, backend, "<Keyboard>/1");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            Assert.That(director.GetVariable(NarrativeSlicesIds.GalleryLoreVariableId).IntValue, Is.EqualTo(1));
            Record("dialogue", "grant node committed: gallery_lore=1, signal slice.gate.granted emitted");

            StartSlice(engine, NarrativeSlicesIds.SliceDialogueGate);
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "sealed line"), 30);
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
            TickUntil(engine, () => director.HasActiveDialogue && UiContains(uiRoot, "sets the counter"), 30);
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

        [Test]
        public void TaskRulesSlice_AnyCompletionRule_CompletesOnSingleSignal()
        {
            using GameEngine engine = CreateEngine();
            NarrativeDirector director = GetDirector(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            StartSlice(engine, NarrativeSlicesIds.SliceTaskRules);
            TickUntil(engine, () => TaskStateOf(engine, NarrativeSlicesIds.RulesAnyCheckTaskId) == TaskInstanceState.Active, 30);
            Record("task", "Slice.Rules.AnyCheck active after slice start (automatic policy)");

            director.EmitSignal(NarrativeSlicesIds.SignalRulesSecond);
            TickUntil(engine, () => TaskStateOf(engine, NarrativeSlicesIds.RulesAnyCheckTaskId) == TaskInstanceState.Completed, 30);
            Record("task", "rules.second alone completed the any-rule task");

            var tasks = engine.GetService(CoreServiceKeys.TaskRuntimeService) as TaskRuntimeService
                ?? throw new InvalidOperationException("TaskRuntimeService was not installed.");
            Assert.That(tasks.Signals.TryGetValue(NarrativeSlicesIds.SignalRulesSecond, out int secondCount) && secondCount == 1, Is.True);
            Assert.That(tasks.Signals.ContainsKey(NarrativeSlicesIds.SignalRulesFirst), Is.False);
            Record("task", "rules.first never emitted; only rules.second counted once");

            var runtime = GetSlicesRuntime(engine);
            var taskTrace = runtime.Events
                .Where(e => e.Phase == "task")
                .Select(e => $"{e.EventName}:{e.Detail}")
                .ToList();
            Assert.That(taskTrace, Does.Contain($"activated:{NarrativeSlicesIds.RulesAnyCheckTaskId}"));
            Assert.That(taskTrace, Does.Contain($"completed:{NarrativeSlicesIds.RulesAnyCheckTaskId}"));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(1));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("rules", "activated/completed traced; slice_counter=1");

            WriteArtifacts(NarrativeSlicesIds.SliceTaskRules);
        }

        [Test]
        public void TaskChainSlice_NextTaskLink_StartsDeclaredCinematic()
        {
            using GameEngine engine = CreateEngine();
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            StartSlice(engine, NarrativeSlicesIds.SliceTaskChain);
            TickUntil(engine, () => TaskStateOf(engine, NarrativeSlicesIds.ChainOneTaskId) == TaskInstanceState.Active, 30);
            Record("task", "Slice.Chain.One active after slice start");

            director.EmitSignal(NarrativeSlicesIds.SignalChainOneDone);
            TickUntil(engine, () => TaskStateOf(engine, NarrativeSlicesIds.ChainTwoTaskId) == TaskInstanceState.Active, 30);
            Record("task", "chain.one.done completed One; next_task_id auto-started Two");

            TickUntil(engine, () => director.HasActiveCinematic && UiContains(uiRoot, "second errand wakes"), 30);
            Record("cinematic", "on_enter_cinematic_id started Cinematic.Slice.ChainIntro when Two activated");

            TickUntil(engine, () => !director.HasActiveCinematic, 30);
            Record("cinematic", "ChainIntro finished");

            Assert.That(TaskStateOf(engine, NarrativeSlicesIds.ChainOneTaskId), Is.EqualTo(TaskInstanceState.Completed));
            Assert.That(TaskStateOf(engine, NarrativeSlicesIds.ChainTwoTaskId), Is.EqualTo(TaskInstanceState.Active));

            var runtime = GetSlicesRuntime(engine);
            var taskTrace = runtime.Events
                .Where(e => e.Phase == "task")
                .Select(e => $"{e.EventName}:{e.Detail}")
                .ToList();
            Assert.That(taskTrace, Does.Contain($"activated:{NarrativeSlicesIds.ChainOneTaskId}"));
            Assert.That(taskTrace, Does.Contain($"completed:{NarrativeSlicesIds.ChainOneTaskId}"));
            Assert.That(taskTrace, Does.Contain($"activated:{NarrativeSlicesIds.ChainTwoTaskId}"));

            var cinematicTrace = runtime.Events
                .Where(e => e.Phase == "cinematic")
                .Select(e => $"{e.EventName}:{e.Detail}")
                .ToList();
            Assert.That(cinematicTrace.Any(t => t.StartsWith(
                $"step_entered:{NarrativeSlicesIds.ChainIntroCinematicId}/", StringComparison.Ordinal)), Is.True);
            Assert.That(cinematicTrace, Does.Contain($"completed:{NarrativeSlicesIds.ChainIntroCinematicId}"));
            director.EmitSignal("chain.two.done");
            TickUntil(engine, () => TaskStateOf(engine, NarrativeSlicesIds.ChainTwoTaskId) == TaskInstanceState.Completed, 30);
            Record("chain", "the second errand is seen through; the page closes");

            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(1));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("chain", "task chain + declared cinematic link traced; slice_counter=1");

            WriteArtifacts(NarrativeSlicesIds.SliceTaskChain);
        }

        [Test]
        public void ActivityExecuteConditionSlice_OptionExecutabilityContract()
        {
            using GameEngine engine = CreateEngine();

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            StartSlice(engine, NarrativeSlicesIds.SliceActivityExecuteCondition);
            TickUntil(engine, () => FindActivityView(engine, NarrativeSlicesIds.ActivitySliceExecuteId) != null, 30);
            ActivityView view = FindActivityView(engine, NarrativeSlicesIds.ActivitySliceExecuteId)
                ?? throw new InvalidOperationException("Slice.Execute activity was not offered.");
            Assert.That(view.State, Is.EqualTo(ActivityInstanceState.Active));
            Assert.That(view.DispatchPolicy, Is.EqualTo(ActivityDispatchPolicy.Forced));
            Record("activity", "forced activity Slice.Execute offered through the slice conductor");

            var activities = engine.GetService(CoreServiceKeys.ActivityRuntimeService) as ActivityRuntimeService
                ?? throw new InvalidOperationException("ActivityRuntimeService was not installed.");
            var options = new List<ActivityOptionView>();
            Assert.That(activities.TryGetActiveOptions(view.Entity, null, options), Is.True);
            ActivityOptionView optGo = options.Single(o => string.Equals(o.OptionId, NarrativeSlicesIds.ActivityOptionGoId, StringComparison.Ordinal));
            ActivityOptionView optWait = options.Single(o => string.Equals(o.OptionId, NarrativeSlicesIds.ActivityOptionWaitId, StringComparison.Ordinal));
            Assert.That(optGo.Executable, Is.True);
            Assert.That(optGo.BlockReason, Is.Empty);
            Assert.That(optWait.IsBaseline, Is.True);
            Record("activity", "opt_go Executable=true with empty BlockReason; opt_wait is the baseline");

            activities.ResolveOption(view.Entity, NarrativeSlicesIds.ActivityOptionGoId);
            Assert.That(TaskStateOf(engine, NarrativeSlicesIds.RulesAnyCheckTaskId), Is.EqualTo(TaskInstanceState.Active));
            Record("activity", "resolving executable opt_go ran its task.create effect");

            Entity secondScope = engine.World.Create();
            Entity second = activities.OfferOrActivate(NarrativeSlicesIds.ActivitySliceExecuteId, secondScope);
            activities.ResolveOption(second, NarrativeSlicesIds.ActivityOptionWaitId);
            Assert.That(activities.TryGetState(second, out ActivityInstanceState settled, out string settledId), Is.True);
            Assert.That(settled, Is.EqualTo(ActivityInstanceState.Resolved));
            Assert.That(settledId, Is.EqualTo(NarrativeSlicesIds.ActivitySliceExecuteId));
            Record("activity", "baseline opt_wait resolved the second instance without effects");

            const string gatedOptionJson = """
                {
                  "id": "opt_go",
                  "title": "Open the rules check",
                  "execute_condition": { "condition_key": "task.counter_below", "parameters": { "max": 3 } }
                }
                """;
            var loadTimeProviders = new ProviderServices();
            using JsonDocument doc = JsonDocument.Parse(gatedOptionJson);
            var references = ProviderDefinitionValidator.CollectFromJsonDocument(
                NarrativeSlicesIds.ActivitySliceExecuteId, doc.RootElement);
            InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(
                () => loadTimeProviders.Validator.ValidateAndThrow(references))!;
            Assert.That(rejected.Message, Does.Contain(ProviderFailureCodes.UnknownProviderKey));
            Assert.That(rejected.Message, Does.Contain("task.counter_below"));
            Record("activity", "full path attempt: execute_condition with an unregistered condition key fails provider validation at load (fail-fast, no fallback)");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            WriteArtifacts(NarrativeSlicesIds.SliceActivityExecuteCondition, ActivityConditionOpenIssues);
        }

        [Test]
        public void SubtitlePresenterSlice_CinematicStepsReplaceSubtitleText()
        {
            using GameEngine engine = CreateEngine();
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            string[] stepTexts = { "Page one", "Page two", "Page three" };
            StartSlice(engine, NarrativeSlicesIds.SliceSubtitlePresenter);
            TickUntil(engine, () => director.HasActiveCinematic && UiContains(uiRoot, stepTexts[0]), 30);
            Record("subtitle", "step 1 text visible on the presenter chain");

            for (int i = 1; i < stepTexts.Length; i++)
            {
                int previous = i - 1;
                TickUntil(engine, () => UiContains(uiRoot, stepTexts[i]), 30);
                Assert.That(UiContains(uiRoot, stepTexts[previous]), Is.False,
                    $"Step {previous + 1} text must be gone once step {i + 1} is presented.");
                Record("subtitle", $"step {i + 1} text replaces step {previous + 1}");
            }

            TickUntil(engine, () => !director.HasActiveCinematic, 30);
            TickUntil(engine, () => stepTexts.All(text => !UiContains(uiRoot, text)), 30);
            Record("subtitle", "cinematic finished; all three step texts cleared from the UI");

            var runtime = GetSlicesRuntime(engine);
            var stepTrace = runtime.Events
                .Where(e => e.Phase == "cinematic" && e.EventName == "step_entered")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(stepTrace.Count, Is.EqualTo(3));
            Assert.That(stepTrace[0], Does.Contain(stepTexts[0]));
            Assert.That(stepTrace[1], Does.Contain(stepTexts[1]));
            Assert.That(stepTrace[2], Does.Contain(stepTexts[2]));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(1));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("subtitle", "three step_entered events in order; slice_counter=1");

            WriteArtifacts(NarrativeSlicesIds.SliceSubtitlePresenter);
        }

        [Test]
        public void PresenterTrackSlice_StepImpulsesAndCameraTrack()
        {
            using GameEngine engine = CreateEngine();
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            var runtime = GetSlicesRuntime(engine);
            StartSlice(engine, NarrativeSlicesIds.SlicePresenterTrack);
            TickUntil(engine, () => director.HasActiveCinematic && UiContains(uiRoot, "far glass and the ridge goes wide"), 30);
            Assert.That(runtime.TrackImpulseCount, Is.EqualTo(1));
            Record("track", "step 1 boundary: one presenter impulse emitted");

            TickUntil(engine, () => UiContains(uiRoot, "close glass locks onto a single lamp"), 30);
            Assert.That(runtime.TrackImpulseCount, Is.EqualTo(2));
            Record("track", "step 2 boundary: impulse count incremented to 2");

            TickUntil(engine, () => UiContains(uiRoot, "The glasses come down"), 30);
            Assert.That(runtime.TrackImpulseCount, Is.EqualTo(3));
            Record("track", "step 3 boundary: impulse count incremented to 3");

            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.GalleryInspectCameraId, 120);
            Record("camera", $"step 2 cameraId observed live: brain active id='{NarrativeSlicesIds.GalleryInspectCameraId}'");

            TickUntil(engine, () => !director.HasActiveCinematic, 30);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 120);
            Record("camera", $"cinematic finished: brain fell back to '{NarrativeSlicesIds.MapDefaultCameraId}'");

            var commandTrace = runtime.Events
                .Where(e => e.EventName == "presenter_command")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(commandTrace.Count, Is.EqualTo(3));
            Assert.That(commandTrace[0], Does.Contain("track_1"));
            Assert.That(commandTrace[1], Does.Contain("track_2"));
            Assert.That(commandTrace[2], Does.Contain("track_3"));
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(1));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("track", "presenter command track complete; slice_counter=1");

            WriteArtifacts(NarrativeSlicesIds.SlicePresenterTrack);
        }

        [Test]
        public void MapVariableWriteSlice_ParityBranchOnCounterValue()
        {
            using GameEngine engine = CreateEngine();
            var backend = GetInputBackend(engine);
            NarrativeDirector director = GetDirector(engine);
            UIRoot uiRoot = GetUiRoot(engine);

            _timeline.Clear();
            LoadMap(engine);
            TickUntil(engine, () => ActiveCameraId(engine) == NarrativeSlicesIds.MapDefaultCameraId, 30);
            Record("map", $"hub loaded; default camera '{NarrativeSlicesIds.MapDefaultCameraId}' active");

            StartSlice(engine, NarrativeSlicesIds.SliceMapVariableWrite);
            TickUntil(engine, () => UiContains(uiRoot, "lands on two"), 30);
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(2));
            Assert.That(UiContains(uiRoot, "tips to three"), Is.False);
            Record("map", "trigger dialogue emitted slice.map.write; counter 1+1=2 (even) opened Dialogue.Slice.MapEven");

            var runtime = GetSlicesRuntime(engine);
            var signalTrace = runtime.Events
                .Where(e => e.EventName == "signal")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(signalTrace, Does.Contain(NarrativeSlicesIds.SignalMapWrite));

            PressButton(engine, backend, "<Keyboard>/enter");
            TickUntil(engine, () => !director.HasActiveDialogue, 30);
            Record("dialogue", "MapEven closed by advance");

            director.EmitSignal(NarrativeSlicesIds.SignalMapWrite);
            TickUntil(engine, () => UiContains(uiRoot, "tips to three"), 30);
            Assert.That(engine.CurrentMapSession?.Variables?.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter), Is.EqualTo(3));
            Assert.That(UiContains(uiRoot, "lands on two"), Is.False);
            Record("map", "second signal: counter 2+1=3 (odd) opened Dialogue.Slice.MapOdd; parity flipped");

            var mapTrace = runtime.Events
                .Where(e => e.Phase == "map" && e.EventName == "map_variable_written")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(mapTrace.Count, Is.EqualTo(2));
            Assert.That(mapTrace[0], Does.Contain("slice_counter=2"));
            Assert.That(mapTrace[0], Does.Contain("parity=even"));
            Assert.That(mapTrace[1], Does.Contain("slice_counter=3"));
            Assert.That(mapTrace[1], Does.Contain("parity=odd"));

            var parityTrace = runtime.Events
                .Where(e => e.EventName == "parity_dialogue_started")
                .Select(e => e.Detail)
                .ToList();
            Assert.That(parityTrace, Is.EqualTo(new[] { NarrativeSlicesIds.MapEvenDialogueId, NarrativeSlicesIds.MapOddDialogueId }));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Record("map", "map variable read/write traced as chain decision input");

            WriteArtifacts(NarrativeSlicesIds.SliceMapVariableWrite);
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

        private static ActivityView? FindActivityView(GameEngine engine, string activityId)
        {
            if (engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
            {
                return null;
            }

            foreach (ActivityView view in activities.CaptureViews())
            {
                if (string.Equals(view.ActivityId, activityId, StringComparison.Ordinal))
                {
                    return view;
                }
            }

            return null;
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

        private void WriteArtifacts(string scenario, string? openIssues = null)
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
            if (!string.IsNullOrWhiteSpace(openIssues))
            {
                report.AppendLine("## Open issues");
                report.AppendLine();
                report.AppendLine(openIssues.TrimEnd());
                report.AppendLine();
            }
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
            NarrativeSlicesIds.SliceTaskRules => TaskRulesPathMermaid,
            NarrativeSlicesIds.SliceTaskChain => TaskChainPathMermaid,
            NarrativeSlicesIds.SliceActivityExecuteCondition => ActivityExecutePathMermaid,
            NarrativeSlicesIds.SliceSubtitlePresenter => SubtitlePathMermaid,
            NarrativeSlicesIds.SlicePresenterTrack => TrackPathMermaid,
            NarrativeSlicesIds.SliceMapVariableWrite => MapWritePathMermaid,
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

        private const string TaskRulesPathMermaid = """
            flowchart TD
                start([StartSlice task_rules]) --> active[Slice.Rules.AnyCheck Active: completion_rule any]
                active --> emit[EmitSignal rules.second]
                emit --> done[one bell is enough: task Completed]
                unused[the first bell never rings] -.-> done
                done --> finish([slice finished: AnyCheck=Completed, counter=1])
            """;

        private const string TaskChainPathMermaid = """
            flowchart TD
                start([StartSlice task_chain]) --> one[Slice.Chain.One Active]
                one --> emit[EmitSignal chain.one.done]
                emit --> oneDone[One Completed]
                oneDone -->|next_task_id auto-start| two[Slice.Chain.Two Active]
                two -->|on_enter_cinematic_id| intro[Cinematic.Slice.ChainIntro step chain_intro_1]
                intro --> introDone([cinematic completed; Two stays Active])
            """;

        private const string ActivityExecutePathMermaid = """
            flowchart TD
                start([StartSlice activity_execute_condition]) --> offer[forced activity Slice.Execute offered]
                offer --> opts[TryGetActiveOptions]
                opts --> go[opt_go: Executable=true, BlockReason empty]
                opts --> wait[opt_wait: is_baseline]
                go --> resolveGo[ResolveOption opt_go: task.create effect activates Slice.Rules.AnyCheck]
                wait --> second[second instance offered]
                second --> resolveWait[ResolveOption opt_wait: settles Resolved]
                resolveGo -.->|full path attempt| gap{{execute_condition task.counter_below: unknown_provider_key fail-fast at load}}
                resolveWait --> finish([contract proven; condition gap documented])
            """;

        private const string SubtitlePathMermaid = """
            flowchart TD
                start([StartSlice subtitle_presenter]) --> s1[SUBTITLE-FIRST visible]
                s1 --> s2[SUBTITLE-SECOND replaces FIRST]
                s2 --> s3[SUBTITLE-THIRD replaces SECOND]
                s3 --> clear([cinematic done: all step texts cleared])
            """;

        private const string TrackPathMermaid = """
            flowchart TD
                start([StartSlice presenter_track]) --> t1[track_1: impulse 1, Tactical holds]
                t1 --> t2[track_2: impulse 2, cameraId Camera.Profile.Inspect]
                t2 -->|brain resolves| inspect[active camera = Inspect]
                t3[track_3: impulse 3] --> done([cinematic done: brain falls back to Tactical])
                inspect --> t3
            """;

        private const string MapWritePathMermaid = """
            flowchart TD
                start([StartSlice map_variable_write]) --> trigger[Dialogue.Slice.MapTrigger onEnter: EmitSignal slice.map.write]
                trigger --> handler[signal handler: slice_counter 1+1=2, parity even]
                handler --> even[Dialogue.Slice.MapEven opened deferred]
                even --> close[advance closes MapEven]
                close --> again[EmitSignal slice.map.write again]
                again --> handler2[signal handler: 2+1=3, parity odd]
                handler2 --> odd([Dialogue.Slice.MapOdd opened; parity flipped])
            """;

        private const string ActivityConditionOpenIssues = """
            - execute_condition 在内容侧没有 condition provider 注册途径（引擎缺口，如实暴露，未绕过）：
              activities.json 由 ActivityConfigLoader 在 GameEngine.InitializeWithConfigPipeline 内加载并做 provider 键校验；
              彼时 ProviderServices 仅含 TaskBridgeProviderInstaller.Install 注册的 task.state_changed(source) 与 task.create(effect)，
              condition 注册表为空。生产初始化路径没有任何 condition provider 注册点：
              FixtureProviderInstaller.InstallMinimal（fixture.always_true）只被测试工程引用，
              ProviderGapCatalog.RegisterFrameworkGaps 也只声明 task.create / task.state_changed 两条框架缺口。
              mod 的 GameStart 订阅在 engine.Start() 才触发，晚于配置加载，注册来不及。
              本方法在生产同构的空 ProviderServices 上复现 ValidateAndThrow 对
              execute_condition "task.counter_below" 的 fail-fast（unknown_provider_key）。
              另注：ProviderKey 的域白名单不含内容自定义域（如 slice），内容侧即使声明 slice.counter_below
              也会先撞 provider_domain_not_allowed。
              需要引擎提供初始化期的 condition provider 注册途径（内置条件族或声明式条件），
              之后 opt_go 的 execute_condition 才能真正接入内容。
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
