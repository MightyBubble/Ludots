using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;

namespace NarrativeChainShowcaseMod.Runtime
{
    /// <summary>
    /// Trigger-driven chain conductor. The narrative domain only emits trigger events and
    /// narrative-domain actions; every cross-domain effect here (activity offer, presenter
    /// impulse, map-variable write) is the reaction of a trigger subscriber, never a
    /// narrative-side write.
    /// </summary>
    public sealed class NarrativeChainRuntime
    {
        private bool _inputActive;

        internal bool PendingCinematic;
        internal int ObjectiveDelayFrames = -1;
        public bool ChainFinished { get; private set; }

        public int PresenterCommandCount { get; private set; }
        public int HeraldEventCount { get; private set; }
        public IReadOnlyList<ChainEvent> Events => _events;

        private readonly List<ChainEvent> _events = new();
        private int _eventSerial;

        public NarrativeChainRuntime()
        {
        }

        internal void Record(string phase, string eventName, string detail)
        {
            _events.Add(new ChainEvent(++_eventSerial, phase, eventName, detail));
        }

        public Task HandleGameStartAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue("NarrativeChain.SystemsInstalled", out object? installed) &&
                installed is bool installedValue && installedValue)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext["NarrativeChain.SystemsInstalled"] = true;
            engine.GlobalContext["NarrativeChain.Runtime"] = this;
            engine.RegisterSystem(new NarrativeChainAdvanceSystem(engine, this), SystemGroup.Cleanup);
            engine.RegisterPresentationSystem(new NarrativeChainPanelPresentationSystem(engine));
            Record("boot", "systems_installed", "advance + panel systems registered");
            return Task.CompletedTask;
        }

        public Task HandleMapLoadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsChainMap(engine))
            {
                return Task.CompletedTask;
            }

            PushInputContext(engine);
            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                throw new InvalidOperationException("NarrativeDirector is required for the chain showcase.");
            }

            director.StartDialogue(NarrativeChainIds.OpeningDialogueId);
            Record("map", "dialogue_started", NarrativeChainIds.OpeningDialogueId);
            return Task.CompletedTask;
        }

        public Task HandleTaskSignalAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsChainMap(engine))
            {
                return Task.CompletedTask;
            }

            string signalId = context.Get(TaskServiceKeys.SignalId) ?? string.Empty;
            switch (signalId)
            {
                case NarrativeChainIds.SignalOpened:
                    PendingCinematic = true;
                    Record("dialogue", "signal", $"{signalId} -> cinematic pending");
                    break;

                case NarrativeChainIds.SignalSetAlarm:
                    WriteMapAlarm(engine);
                    Record("verdict", "map_variable_written",
                        $"{NarrativeChainIds.MapVariableAlarms}={engine.CurrentMapSession?.Variables?.ReadInt(NarrativeChainIds.MapVariableAlarms)} (via trigger)");
                    break;

                case NarrativeChainIds.SignalHerald:
                    HeraldEventCount++;
                    EmitPresenterImpulse(engine);
                    Record("verdict", "event_broadcast", $"{signalId} -> camera impulse reaction");
                    break;

                case NarrativeChainIds.SignalFinished:
                    ChainFinished = true;
                    Record("verdict", "signal", signalId);
                    break;
            }

            return Task.CompletedTask;
        }

        public Task HandleCinematicStepEnteredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsChainMap(engine))
            {
                return Task.CompletedTask;
            }

            string cinematicId = context.Get(NarrativeServiceKeys.CinematicId) ?? string.Empty;
            if (!string.Equals(cinematicId, NarrativeChainIds.RevealCinematicId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            EmitPresenterImpulse(engine);
            PresenterCommandCount++;
            Record("cinematic", "presenter_command",
                $"step={context.Get(NarrativeServiceKeys.CinematicStepId)} subtitle=\"{context.Get(NarrativeServiceKeys.BodyText)}\"");
            return Task.CompletedTask;
        }

        public Task HandleCinematicCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsChainMap(engine))
            {
                return Task.CompletedTask;
            }

            string cinematicId = context.Get(NarrativeServiceKeys.CinematicId) ?? string.Empty;
            if (!string.Equals(cinematicId, NarrativeChainIds.RevealCinematicId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            if (engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
            {
                throw new InvalidOperationException("ActivityRuntimeService is required for the chain showcase.");
            }

            Entity scope = engine.World.Create();
            activities.OfferOrActivate(NarrativeChainIds.DecideActivityId, scope);
            Record("cinematic", "activity_offered", NarrativeChainIds.DecideActivityId);
            return Task.CompletedTask;
        }

        public Task HandleTaskActivatedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsChainMap(engine))
            {
                return Task.CompletedTask;
            }

            string taskId = context.Get(TaskServiceKeys.TaskId) ?? string.Empty;
            if (!string.Equals(taskId, NarrativeChainIds.SurveyTaskId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            ObjectiveDelayFrames = 30;
            Record("task", "activated", taskId);
            return Task.CompletedTask;
        }

        public Task HandleTaskCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                !IsChainMap(engine))
            {
                return Task.CompletedTask;
            }

            string taskId = context.Get(TaskServiceKeys.TaskId) ?? string.Empty;
            if (!string.Equals(taskId, NarrativeChainIds.SurveyTaskId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return Task.CompletedTask;
            }

            director.StartDialogue(NarrativeChainIds.VerdictDialogueId);
            Record("task", "completed->dialogue", NarrativeChainIds.VerdictDialogueId);
            return Task.CompletedTask;
        }

        internal void EmitObjectiveSignal(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            director.EmitSignal(NarrativeChainIds.SignalObjectiveDone);
            Record("task", "objective_signal", NarrativeChainIds.SignalObjectiveDone);
        }

        private void PushInputContext(GameEngine engine)
        {
            if (_inputActive || engine.GetService(CoreServiceKeys.InputHandler) is not Ludots.Core.Input.Runtime.PlayerInputHandler input)
            {
                return;
            }

            input.PushContext(NarrativeChainIds.InputContextId);
            _inputActive = true;
        }

        private void WriteMapAlarm(GameEngine engine)
        {
            var variables = engine.CurrentMapSession?.Variables;
            if (variables == null || !variables.Contains(NarrativeChainIds.MapVariableAlarms))
            {
                throw new InvalidOperationException(
                    $"Map variable '{NarrativeChainIds.MapVariableAlarms}' is not declared on '{NarrativeChainIds.MapId}'.");
            }

            variables.WriteInt(NarrativeChainIds.MapVariableAlarms, variables.ReadInt(NarrativeChainIds.MapVariableAlarms) + 1);
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

        private static bool IsChainMap(GameEngine engine) =>
            string.Equals(engine.CurrentMapSession?.MapId.Value, NarrativeChainIds.MapId, StringComparison.OrdinalIgnoreCase);

        public readonly record struct ChainEvent(int Serial, string Phase, string EventName, string Detail);
    }
}
