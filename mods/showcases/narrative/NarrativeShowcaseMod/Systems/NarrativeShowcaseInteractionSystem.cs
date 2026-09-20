using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;
using NarrativeShowcaseMod.Runtime;

namespace NarrativeShowcaseMod.Systems
{
    internal sealed class NarrativeShowcaseInteractionSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NarrativeShowcaseRuntime _runtime;

        internal NarrativeShowcaseInteractionSystem(GameEngine engine, NarrativeShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!_runtime.IsShowcaseActive(_engine) ||
                _engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue ||
                _engine.GetService(CoreServiceKeys.SequencerRuntime) is not SequencerRuntime sequencer ||
                _engine.GetService(CoreServiceKeys.TaskRuntimeService) is not TaskRuntimeService tasks)
            {
                return;
            }

            _runtime.RebindEntities(_engine);
            TrackBeastDefeat(dialogue, tasks);

            var input = _engine.GetService(CoreServiceKeys.AuthoritativeInput);
            if (input == null ||
                !input.PressedThisFrame(DialogueInputActionIds.Interact) ||
                dialogue.HasActiveDialogue ||
                sequencer.HasActiveSequence)
            {
                return;
            }

            if (!dialogue.TryResolveEntity(NarrativeShowcaseIds.PlayerAlias, out Entity player) ||
                !_engine.World.TryGet(player, out WorldPositionCm playerPos))
            {
                return;
            }

            if (TryInteractElder(dialogue, tasks, playerPos))
            {
                return;
            }

            TryInteractShrine(dialogue, sequencer, tasks, playerPos);
        }

        private bool TryInteractElder(DialogueRuntime dialogue, TaskRuntimeService tasks, WorldPositionCm playerPos)
        {
            if (!dialogue.TryResolveEntity(NarrativeShowcaseIds.ElderAlias, out Entity elder) ||
                !_engine.World.TryGet(elder, out WorldPositionCm elderPos) ||
                !IsNear(playerPos, elderPos, _runtime.WardenInteractRangeCm))
            {
                return false;
            }

            if (tasks.TryGetState(NarrativeShowcaseIds.BriefingTaskId, out var briefingState) &&
                briefingState == TaskInstanceState.Active)
            {
                dialogue.StartDialogue(NarrativeShowcaseIds.BriefingDialogueId);
                return true;
            }

            if (tasks.TryGetState(NarrativeShowcaseIds.ReturnTaskId, out var returnState) &&
                returnState == TaskInstanceState.Active)
            {
                dialogue.StartDialogue(NarrativeShowcaseIds.ReturnDialogueId);
                return true;
            }

            return false;
        }

        private void TryInteractShrine(
            DialogueRuntime dialogue,
            SequencerRuntime sequencer,
            TaskRuntimeService tasks,
            WorldPositionCm playerPos)
        {
            if (_runtime.BeastSpawned(_engine))
            {
                return;
            }

            if (!tasks.TryGetState(NarrativeShowcaseIds.TrialTaskId, out var trialState) ||
                trialState != TaskInstanceState.Active)
            {
                return;
            }

            if (!dialogue.TryResolveEntity(NarrativeShowcaseIds.ShrineAlias, out Entity shrine) ||
                !_engine.World.TryGet(shrine, out WorldPositionCm shrinePos) ||
                !IsNear(playerPos, shrinePos, _runtime.ShrineInteractRangeCm))
            {
                return;
            }

            sequencer.Start(NarrativeShowcaseIds.TrialRevealSequenceId);
        }

        private void TrackBeastDefeat(DialogueRuntime dialogue, TaskRuntimeService tasks)
        {
            if (_runtime.BeastDefeated(_engine) ||
                !dialogue.TryResolveEntity(NarrativeShowcaseIds.BeastAlias, out Entity beast))
            {
                return;
            }

            if (!_engine.World.TryGet(beast, out AttributeBuffer attributes))
            {
                return;
            }

            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Health");
            if (healthId != Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId &&
                attributes.GetCurrent(healthId) <= 0f)
            {
                _runtime.MarkBeastDefeated(_engine);
                _runtime.EmitShowcaseSignal(_engine, tasks, NarrativeShowcaseIds.BeastDefeatedSignal);
            }
        }

        private static bool IsNear(WorldPositionCm a, WorldPositionCm b, float rangeCm)
        {
            Vector2 va = a.Value.ToVector2();
            Vector2 vb = b.Value.ToVector2();
            return Vector2.Distance(va, vb) <= rangeCm;
        }
    }
}
