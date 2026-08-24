using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;

namespace NarrativeSlicesMod.Runtime
{
    /// <summary>
    /// Slice conductor for the narrative slices showcase. Each slice is one self-contained
    /// scenario started through <see cref="StartSlice"/>; slice content starts are deferred to
    /// the Cleanup-phase advance system so a slice can never re-enter the NarrativeDirector
    /// from inside a trigger handler.
    /// </summary>
    public sealed class NarrativeSlicesRuntime
    {
        private bool _inputActive;
        private string? _pendingSliceId;
        private string? _pendingParityDialogueId;

        public int TrackImpulseCount { get; private set; }

        public IReadOnlyList<SliceEvent> Events => _events;

        private readonly List<SliceEvent> _events = new();
        private int _eventSerial;

        public NarrativeSlicesRuntime()
        {
        }

        internal void Record(string phase, string eventName, string detail)
        {
            _events.Add(new SliceEvent(++_eventSerial, phase, eventName, detail));
        }

        public void StartSlice(string sliceId)
        {
            if (string.IsNullOrWhiteSpace(sliceId))
            {
                throw new ArgumentException("Slice id is required.", nameof(sliceId));
            }

            ValidateSliceId(sliceId);
            if (_pendingSliceId != null)
            {
                throw new InvalidOperationException(
                    $"Slice start '{_pendingSliceId}' is still pending; a slice must settle before the next starts.");
            }

            _pendingSliceId = sliceId;
            Record("slice", "start_requested", sliceId);
        }

        internal void ConsumePendingSlice(GameEngine engine)
        {
            if (_pendingSliceId == null)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                throw new InvalidOperationException("NarrativeDirector is required for narrative slices.");
            }

            if (director.HasActiveDialogue || director.HasActiveCinematic)
            {
                return;
            }

            string sliceId = _pendingSliceId;
            _pendingSliceId = null;
            BumpSliceCounter(engine);
            StartSliceContent(engine, director, sliceId);
            Record("slice", "content_started", sliceId);
        }

        internal void ConsumePendingParityDialogue(GameEngine engine)
        {
            if (_pendingParityDialogueId == null)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                throw new InvalidOperationException("NarrativeDirector is required for narrative slices.");
            }

            if (director.HasActiveDialogue || director.HasActiveCinematic)
            {
                return;
            }

            string dialogueId = _pendingParityDialogueId;
            _pendingParityDialogueId = null;
            director.StartDialogue(dialogueId);
            Record("map", "parity_dialogue_started", dialogueId);
        }

        private static void ValidateSliceId(string sliceId)
        {
            switch (sliceId)
            {
                case NarrativeSlicesIds.SliceDialogueGate:
                case NarrativeSlicesIds.SliceActionGallery:
                case NarrativeSlicesIds.SliceTaskRules:
                case NarrativeSlicesIds.SliceTaskChain:
                case NarrativeSlicesIds.SliceActivityExecuteCondition:
                case NarrativeSlicesIds.SliceSubtitlePresenter:
                case NarrativeSlicesIds.SlicePresenterTrack:
                case NarrativeSlicesIds.SliceMapVariableWrite:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown narrative slice '{sliceId}'.");
            }
        }

        private static void StartSliceContent(GameEngine engine, NarrativeDirector director, string sliceId)
        {
            switch (sliceId)
            {
                case NarrativeSlicesIds.SliceDialogueGate:
                    director.StartDialogue(NarrativeSlicesIds.GateDialogueId);
                    break;
                case NarrativeSlicesIds.SliceActionGallery:
                    director.StartDialogue(NarrativeSlicesIds.GalleryDialogueId);
                    break;
                case NarrativeSlicesIds.SliceTaskRules:
                    director.StartTask(NarrativeSlicesIds.RulesAnyCheckTaskId);
                    break;
                case NarrativeSlicesIds.SliceTaskChain:
                    director.StartTask(NarrativeSlicesIds.ChainOneTaskId);
                    break;
                case NarrativeSlicesIds.SliceActivityExecuteCondition:
                    OfferExecuteConditionActivity(engine);
                    break;
                case NarrativeSlicesIds.SliceSubtitlePresenter:
                    director.StartCinematic(NarrativeSlicesIds.SubtitleCinematicId);
                    break;
                case NarrativeSlicesIds.SlicePresenterTrack:
                    director.StartCinematic(NarrativeSlicesIds.TrackCinematicId);
                    break;
                case NarrativeSlicesIds.SliceMapVariableWrite:
                    director.StartDialogue(NarrativeSlicesIds.MapTriggerDialogueId);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown narrative slice '{sliceId}'.");
            }
        }

        private static void OfferExecuteConditionActivity(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
            {
                throw new InvalidOperationException("ActivityRuntimeService is required for narrative slices.");
            }

            activities.OfferOrActivate(NarrativeSlicesIds.ActivitySliceExecuteId, engine.World.Create());
        }

        private void BumpSliceCounter(GameEngine engine)
        {
            var variables = engine.CurrentMapSession?.Variables;
            if (variables == null || !variables.Contains(NarrativeSlicesIds.MapVariableSliceCounter))
            {
                throw new InvalidOperationException(
                    $"Map variable '{NarrativeSlicesIds.MapVariableSliceCounter}' is not declared on '{NarrativeSlicesIds.MapId}'.");
            }

            variables.WriteInt(
                NarrativeSlicesIds.MapVariableSliceCounter,
                variables.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter) + 1);
            Record("slice", "map_variable_written",
                $"{NarrativeSlicesIds.MapVariableSliceCounter}={variables.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter)}");
        }

        public Task HandleGameStartAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue("NarrativeSlices.SystemsInstalled", out object? installed) &&
                installed is bool installedValue && installedValue)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext["NarrativeSlices.SystemsInstalled"] = true;
            engine.GlobalContext["NarrativeSlices.Runtime"] = this;
            engine.RegisterSystem(new NarrativeSlicesAdvanceSystem(engine, this), SystemGroup.Cleanup);
            engine.RegisterPresentationSystem(new NarrativeSlicesPanelPresentationSystem(engine));
            Record("boot", "systems_installed", "advance + panel systems registered");
            return Task.CompletedTask;
        }

        public Task HandleMapLoadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            PushInputContext(engine);
            Record("map", "hub_loaded", engine.CurrentMapSession?.MapId.Value ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task HandleDialogueNodeEnteredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            string dialogueId = context.Get(NarrativeServiceKeys.DialogueId) ?? string.Empty;
            Record("dialogue", "node_entered",
                $"{dialogueId}/{context.Get(NarrativeServiceKeys.DialogueNodeId)} body=\"{context.Get(NarrativeServiceKeys.BodyText)}\"");

            var cameraRequest = engine.GetService(CoreServiceKeys.VirtualCameraRequest);
            if (cameraRequest != null)
            {
                string cameraDetail = cameraRequest.Clear
                    ? $"clear node={context.Get(NarrativeServiceKeys.DialogueNodeId)}"
                    : $"activate id={cameraRequest.Id} node={context.Get(NarrativeServiceKeys.DialogueNodeId)}";
                Record("camera", "virtual_camera_request", cameraDetail);
            }

            return Task.CompletedTask;
        }

        public Task HandleDialogueChoiceCommittedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("dialogue", "choice_committed",
                $"{context.Get(NarrativeServiceKeys.DialogueId)}/{context.Get(NarrativeServiceKeys.DialogueNodeId)} choice={context.Get(NarrativeServiceKeys.DialogueChoiceId)}");
            return Task.CompletedTask;
        }

        public Task HandleCinematicStepEnteredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            string cinematicId = context.Get(NarrativeServiceKeys.CinematicId) ?? string.Empty;
            Record("cinematic", "step_entered",
                $"{cinematicId}/{context.Get(NarrativeServiceKeys.CinematicStepId)} body=\"{context.Get(NarrativeServiceKeys.BodyText)}\"");

            if (string.Equals(cinematicId, NarrativeSlicesIds.TrackCinematicId, StringComparison.Ordinal))
            {
                EmitPresenterImpulse(engine);
                TrackImpulseCount++;
                Record("cinematic", "presenter_command",
                    $"step={context.Get(NarrativeServiceKeys.CinematicStepId)} impulse={TrackImpulseCount}");
            }

            return Task.CompletedTask;
        }

        public Task HandleCinematicCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("cinematic", "completed", context.Get(NarrativeServiceKeys.CinematicId) ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task HandleTaskSignalAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            string signalId = context.Get(TaskServiceKeys.SignalId) ?? string.Empty;
            Record("signal", "signal", signalId);
            if (string.Equals(signalId, NarrativeSlicesIds.SignalMapWrite, StringComparison.Ordinal))
            {
                AdvanceMapCounterAndQueueParityDialogue(engine);
            }

            return Task.CompletedTask;
        }

        public Task HandleTaskOfferedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("task", "offered", context.Get(TaskServiceKeys.TaskId) ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task HandleTaskActivatedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("task", "activated", context.Get(TaskServiceKeys.TaskId) ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task HandleTaskCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("task", "completed", context.Get(TaskServiceKeys.TaskId) ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task HandleTaskFailedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("task", "failed", context.Get(TaskServiceKeys.TaskId) ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task HandleTaskAbandonedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsSlicesMap(engine))
            {
                return Task.CompletedTask;
            }

            Record("task", "abandoned", context.Get(TaskServiceKeys.TaskId) ?? string.Empty);
            return Task.CompletedTask;
        }

        private void AdvanceMapCounterAndQueueParityDialogue(GameEngine engine)
        {
            var variables = engine.CurrentMapSession?.Variables;
            if (variables == null || !variables.Contains(NarrativeSlicesIds.MapVariableSliceCounter))
            {
                throw new InvalidOperationException(
                    $"Map variable '{NarrativeSlicesIds.MapVariableSliceCounter}' is not declared on '{NarrativeSlicesIds.MapId}'.");
            }

            int updated = variables.ReadInt(NarrativeSlicesIds.MapVariableSliceCounter) + 1;
            variables.WriteInt(NarrativeSlicesIds.MapVariableSliceCounter, updated);
            bool even = updated % 2 == 0;
            _pendingParityDialogueId = even
                ? NarrativeSlicesIds.MapEvenDialogueId
                : NarrativeSlicesIds.MapOddDialogueId;
            Record("map", "map_variable_written",
                $"{NarrativeSlicesIds.MapVariableSliceCounter}={updated} parity={(even ? "even" : "odd")}");
        }

        private void EmitPresenterImpulse(GameEngine engine)
        {
            var impulse = engine.GetService(CoreServiceKeys.CameraImpulseRuntime);
            impulse.Emit(new CameraImpulseSource
            {
                DurationSeconds = 0.15f,
                FrequencyHz = 8f,
                PositionAmplitudeCm = 4f,
                YawAmplitudeDeg = 0.8f,
                PitchAmplitudeDeg = 0.5f,
                RadiusCm = 200000f,
            });
        }

        private void PushInputContext(GameEngine engine)
        {
            if (_inputActive || engine.GetService(CoreServiceKeys.InputHandler) is not Ludots.Core.Input.Runtime.PlayerInputHandler input)
            {
                return;
            }

            input.PushContext(NarrativeSlicesIds.InputContextId);
            _inputActive = true;
        }

        private static bool IsSlicesMap(GameEngine engine) =>
            string.Equals(engine.CurrentMapSession?.MapId.Value, NarrativeSlicesIds.MapId, StringComparison.OrdinalIgnoreCase);

        public readonly record struct SliceEvent(int Serial, string Phase, string EventName, string Detail);
    }
}
