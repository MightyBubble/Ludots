using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Narrative;
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
            if (!_runtime.IsShowcaseActive(_engine) || _engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            _runtime.RebindEntities(_engine);
            TrackBeastDefeat(director);

            var input = _engine.GetService(CoreServiceKeys.AuthoritativeInput);
            if (input == null || !input.PressedThisFrame(NarrativeInputActionIds.Interact) || director.HasActiveDialogue || director.HasActiveCinematic)
            {
                return;
            }

            if (!director.TryResolveEntity(NarrativeShowcaseIds.PlayerAlias, out Entity player) ||
                !_engine.World.TryGet(player, out WorldPositionCm playerPos))
            {
                return;
            }

            if (TryInteractElder(director, playerPos))
            {
                return;
            }

            TryInteractShrine(director, playerPos);
        }

        private bool TryInteractElder(NarrativeDirector director, WorldPositionCm playerPos)
        {
            if (!director.TryResolveEntity(NarrativeShowcaseIds.ElderAlias, out Entity elder) ||
                !_engine.World.TryGet(elder, out WorldPositionCm elderPos) ||
                !IsNear(playerPos, elderPos, 420f))
            {
                return false;
            }

            if (director.TryGetTaskState(NarrativeShowcaseIds.BriefingTaskId, out var briefingState) &&
                briefingState == TaskInstanceState.Active)
            {
                director.StartDialogue(NarrativeShowcaseIds.BriefingDialogueId);
                return true;
            }

            if (director.TryGetTaskState(NarrativeShowcaseIds.ReturnTaskId, out var returnState) &&
                returnState == TaskInstanceState.Active)
            {
                director.StartDialogue(NarrativeShowcaseIds.ReturnDialogueId);
                return true;
            }

            return false;
        }

        private void TryInteractShrine(NarrativeDirector director, WorldPositionCm playerPos)
        {
            if (_runtime.BeastSpawned(_engine))
            {
                return;
            }

            if (!director.TryGetTaskState(NarrativeShowcaseIds.TrialTaskId, out var trialState) ||
                trialState != TaskInstanceState.Active)
            {
                return;
            }

            if (!director.TryResolveEntity(NarrativeShowcaseIds.ShrineAlias, out Entity shrine) ||
                !_engine.World.TryGet(shrine, out WorldPositionCm shrinePos) ||
                !IsNear(playerPos, shrinePos, 360f))
            {
                return;
            }

            director.StartCinematic(NarrativeShowcaseIds.TrialRevealCinematicId);
        }

        private void TrackBeastDefeat(NarrativeDirector director)
        {
            if (_runtime.BeastDefeated(_engine) || !director.TryResolveEntity(NarrativeShowcaseIds.BeastAlias, out Entity beast))
            {
                return;
            }

            if (!_engine.World.TryGet(beast, out AttributeBuffer attributes))
            {
                return;
            }

            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Health");
            if (healthId != Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId && attributes.GetCurrent(healthId) <= 0f)
            {
                _runtime.MarkBeastDefeated(_engine);
                director.EmitSignal(NarrativeShowcaseIds.BeastDefeatedSignal);
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
