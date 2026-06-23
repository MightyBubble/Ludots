using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Systems
{
    public sealed class RtsSelectionFeedbackPresentationSystem : ISystem<float>
    {
        private const string PrimaryRingKey = "rts.selection.primary_ring";
        private const string QueueRingKey = "rts.selection.queue_ring";
        private const string ProgressBarKey = "rts.selection.progress_bar";

        private readonly GameEngine _engine;
        private readonly PresentationWorldFactPublisher _facts;
        private readonly AbilityDefinitionRegistry? _abilityDefinitions;
        private Entity _activePrimaryOwner = Entity.Null;
        private Entity _activeQueueOwner = Entity.Null;
        private Entity _activeProgressOwner = Entity.Null;
        private int _activePrimaryScope;
        private int _activeQueueScope;
        private int _activeProgressScope;
        private float _elapsedSeconds;

        public RtsSelectionFeedbackPresentationSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            if (!PresentationWorldFactPublisher.TryCreate(engine.GlobalContext, out _facts))
            {
                throw new InvalidOperationException("PresentationEventStream service is missing.");
            }

            _abilityDefinitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _elapsedSeconds += MathF.Max(0f, dt);
            if (!IsRtsMapActive() ||
                !SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity selected) ||
                !_engine.World.IsAlive(selected) ||
                !TryResolveWorldPosition(selected, out Vector3 center))
            {
                EndAllActiveFacts();
                return;
            }

            bool hasQueue = _engine.World.TryGet(selected, out OrderBuffer orders) &&
                            (orders.HasActive || orders.QueuedCount > 0 || orders.HasPending);

            EmitSelectionRing(selected, center, PrimaryRingKey, ref _activePrimaryOwner, ref _activePrimaryScope, pulseScale: 1f);
            if (hasQueue)
            {
                EmitSelectionRing(selected, center, QueueRingKey, ref _activeQueueOwner, ref _activeQueueScope, pulseScale: 1.18f);
            }
            else
            {
                EndActiveOverlay(QueueRingKey, ref _activeQueueOwner, ref _activeQueueScope);
            }

            if (TryResolveProgress(selected, out float progressRatio))
            {
                EmitProgressBar(selected, center, progressRatio);
            }
            else
            {
                EndActiveHud(ProgressBarKey, ref _activeProgressOwner, ref _activeProgressScope);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EmitSelectionRing(Entity owner, Vector3 center, string key, ref Entity activeOwner, ref int activeScope, float pulseScale)
        {
            EndIfOwnerChanged(key, owner, ref activeOwner, ref activeScope, isHud: false);
            float pulse = 1f + 0.06f * MathF.Sin(_elapsedSeconds * 5.4f);
            activeOwner = owner;
            activeScope = PresentationWorldFactPublisher.ComposeScope(key, owner);
            _facts.PublishWorldOverlayUpdated(
                key,
                owner,
                activeScope,
                center + new Vector3(0f, 0.04f, 0f),
                radiusOrLength: 1.42f * pulseScale * pulse,
                innerRadiusOrWidth: 1.1f * pulseScale,
                borderWidth: 0.05f);
        }

        private void EmitProgressBar(Entity owner, Vector3 center, float progressRatio)
        {
            EndIfOwnerChanged(ProgressBarKey, owner, ref _activeProgressOwner, ref _activeProgressScope, isHud: true);
            _activeProgressOwner = owner;
            _activeProgressScope = PresentationWorldFactPublisher.ComposeScope(ProgressBarKey, owner, discriminator: 1);
            _facts.PublishWorldHudUpdated(
                ProgressBarKey,
                owner,
                _activeProgressScope,
                center + new Vector3(0f, 2.1f, 0f),
                Math.Clamp(progressRatio, 0f, 1f));
        }

        private bool TryResolveProgress(Entity selected, out float progressRatio)
        {
            progressRatio = 0f;
            if (_engine.World.TryGet(selected, out AbilityExecInstance exec))
            {
                int totalTicks = ResolveExecTotalTicks(exec.AbilityId, exec.IsToggleDeactivating);
                if (totalTicks <= 0)
                {
                    totalTicks = Math.Max(exec.CurrentTick, 1);
                }

                progressRatio = Math.Clamp(exec.CurrentTick / (float)Math.Max(1, totalTicks), 0f, 1f);
                return true;
            }

            if (_engine.World.TryGet(selected, out ActiveEffectContainer effects))
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    Entity effectEntity = effects.GetEntity(i);
                    if (!_engine.World.IsAlive(effectEntity) ||
                        !_engine.World.TryGet(effectEntity, out GameplayEffect effect) ||
                        effect.TotalTicks <= 0 ||
                        effect.RemainingTicks <= 0)
                    {
                        continue;
                    }

                    progressRatio = Math.Clamp((effect.TotalTicks - effect.RemainingTicks) / (float)Math.Max(1, effect.TotalTicks), 0f, 1f);
                    return true;
                }
            }

            return false;
        }

        private int ResolveExecTotalTicks(int abilityId, bool isToggleDeactivating)
        {
            if (abilityId <= 0 || _abilityDefinitions == null || !_abilityDefinitions.TryGet(abilityId, out AbilityDefinition definition))
            {
                return 0;
            }

            AbilityExecSpec spec = isToggleDeactivating && definition.HasToggleSpec
                ? definition.ToggleSpec.DeactivateExecSpec
                : definition.ExecSpec;

            int total = 0;
            for (int i = 0; i < spec.ItemCount; i++)
            {
                int endTick = spec.GetTick(i) + Math.Max(0, spec.GetDurationTicks(i));
                if (endTick > total)
                {
                    total = endTick;
                }
            }

            return total;
        }

        private bool TryResolveWorldPosition(Entity selected, out Vector3 center)
        {
            if (_engine.World.TryGet(selected, out VisualTransform visual))
            {
                center = visual.Position;
                return true;
            }

            if (_engine.World.TryGet(selected, out WorldPositionCm worldPosition))
            {
                center = new Vector3(
                    worldPosition.Value.X.ToFloat() * 0.01f,
                    0f,
                    worldPosition.Value.Y.ToFloat() * 0.01f);
                return true;
            }

            center = default;
            return false;
        }

        private void EndAllActiveFacts()
        {
            EndActiveOverlay(PrimaryRingKey, ref _activePrimaryOwner, ref _activePrimaryScope);
            EndActiveOverlay(QueueRingKey, ref _activeQueueOwner, ref _activeQueueScope);
            EndActiveHud(ProgressBarKey, ref _activeProgressOwner, ref _activeProgressScope);
        }

        private void EndIfOwnerChanged(string key, Entity owner, ref Entity activeOwner, ref int activeScope, bool isHud)
        {
            if (activeScope <= 0 || activeOwner == owner)
            {
                return;
            }

            if (isHud)
            {
                EndActiveHud(key, ref activeOwner, ref activeScope);
            }
            else
            {
                EndActiveOverlay(key, ref activeOwner, ref activeScope);
            }
        }

        private void EndActiveOverlay(string key, ref Entity activeOwner, ref int activeScope)
        {
            if (activeScope > 0 && activeOwner != Entity.Null)
            {
                _facts.PublishWorldOverlayEnded(key, activeOwner, activeScope);
            }

            activeOwner = Entity.Null;
            activeScope = 0;
        }

        private void EndActiveHud(string key, ref Entity activeOwner, ref int activeScope)
        {
            if (activeScope > 0 && activeOwner != Entity.Null)
            {
                _facts.PublishWorldHudEnded(key, activeOwner, activeScope);
            }

            activeOwner = Entity.Null;
            activeScope = 0;
        }

        private bool IsRtsMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "rts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tags[i], "rts_showcase", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
