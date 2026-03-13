using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Systems
{
    internal sealed class InteractionShowcaseGasEventTapSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly int _b1AbilityId;
        private readonly int _c1AbilityId;
        private readonly int _c2AbilityId;
        private readonly int _c3AbilityId;
        private int _processedEventCount;
        private int _observedClearVersion = -1;

        public InteractionShowcaseGasEventTapSystem(GameEngine engine)
        {
            _engine = engine;
            _b1AbilityId = AbilityIdRegistry.GetId(InteractionShowcaseIds.B1SelfBuffAbilityId);
            _c1AbilityId = AbilityIdRegistry.GetId(InteractionShowcaseIds.C1HostileUnitDamageAbilityId);
            // C2 hostile/dead-ally failures are blocked locally in autoplay before enqueue today.
            // Keep the tap for any future GAS-native failures on the same ability, including target name propagation.
            _c2AbilityId = AbilityIdRegistry.GetId(InteractionShowcaseIds.C2FriendlyUnitHealAbilityId);
            _c3AbilityId = AbilityIdRegistry.GetId(InteractionShowcaseIds.C3AnyUnitConditionalAbilityId);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        public void Update(in float dt)
        {
            if (_engine.GetService(CoreServiceKeys.GasPresentationEventBuffer) is not GasPresentationEventBuffer events)
            {
                _processedEventCount = 0;
                _observedClearVersion = -1;
                return;
            }

            if (!InteractionShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
            {
                _processedEventCount = events.Count;
                _observedClearVersion = events.ClearVersion;
                return;
            }

            if (events.ClearVersion != _observedClearVersion)
            {
                _processedEventCount = 0;
                _observedClearVersion = events.ClearVersion;
            }

            ReadOnlySpan<GasPresentationEvent> publishedEvents = events.Events;
            if (_processedEventCount > publishedEvents.Length)
            {
                _processedEventCount = 0;
            }

            for (int index = _processedEventCount; index < publishedEvents.Length; index++)
            {
                GasPresentationEvent evt = publishedEvents[index];
                if (evt.Kind != GasPresentationEventKind.CastFailed ||
                    (evt.AbilityId != _b1AbilityId &&
                    evt.AbilityId != _c1AbilityId &&
                    evt.AbilityId != _c2AbilityId &&
                    evt.AbilityId != _c3AbilityId))
                {
                    continue;
                }

                _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailReason] = evt.FailReason.ToString();
                _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailTick] = _engine.GameSession.CurrentTick;
                _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailAttribute] = evt.AttributeId != AttributeRegistry.InvalidId
                    ? AttributeRegistry.GetName(evt.AttributeId)
                    : string.Empty;
                _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailDelta] = evt.Delta;

                string targetName = ResolveTargetName(evt.Target);
                if (!string.IsNullOrEmpty(targetName))
                {
                    _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = targetName;
                }
            }

            _processedEventCount = publishedEvents.Length;
        }

        private string ResolveTargetName(Entity entity)
        {
            if (entity == Entity.Null || !_engine.World.IsAlive(entity) || !_engine.World.Has<Name>(entity))
            {
                return string.Empty;
            }

            return _engine.World.Get<Name>(entity).Value ?? string.Empty;
        }
    }
}
