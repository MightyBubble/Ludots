using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Mirrors same-step GasPresentationEventBuffer ability/effect moments into the
    /// TriggerManager map-scoped event domain ("Ability.CastStarted" … / "Effect.Applied" …).
    /// Runs in ClearPresentationFlags BEFORE GameplayPresentationProjectionSystem: read-only
    /// pass, never clears the buffer — the projection still consumes every moment exactly
    /// once, so no moment is double-fired into presentation.
    /// </summary>
    public sealed class TriggerGraphMomentBridgeSystem : ISystem<float>
    {
        private readonly GasPresentationEventBuffer _gasEvents;
        private readonly TriggerManager _triggerManager;
        private readonly World _world;
        private readonly Func<ScriptContext> _contextFactory;

        public int DroppedNoMapEvents { get; private set; }

        public TriggerGraphMomentBridgeSystem(
            GasPresentationEventBuffer gasEvents,
            TriggerManager triggerManager,
            World world,
            Func<ScriptContext> contextFactory)
        {
            _gasEvents = gasEvents ?? throw new ArgumentNullException(nameof(gasEvents));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            ReadOnlySpan<GasPresentationEvent> events = _gasEvents.Events;
            for (int i = 0; i < events.Length; i++)
            {
                PublishOne(events[i]);
            }
        }

        private void PublishOne(in GasPresentationEvent evt)
        {
            string? eventName = EventNameFor(evt.Kind);
            if (eventName == null)
            {
                return;
            }

            MapId mapId = ResolveMap(evt.Actor);
            if (string.IsNullOrEmpty(mapId.Value))
            {
                DroppedNoMapEvents++;
                return;
            }

            ScriptContext context = _contextFactory();
            context.Set(ContextKeys.MapId, mapId);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, evt.Actor);
            context.Set(MapTriggerEventPayloadKeys.TargetEntity, evt.Target);
            context.Set(MapTriggerEventPayloadKeys.AbilityId, evt.AbilityId);
            context.Set(MapTriggerEventPayloadKeys.EffectId, evt.EffectTemplateId);
            context.Set(MapTriggerEventPayloadKeys.Magnitude, evt.Delta);
            context.Set(MapTriggerEventPayloadKeys.Moment, evt.Kind.ToString());
            _triggerManager.FireMapEvent(mapId, new EventKey(eventName), context);
        }

        internal static string? EventNameFor(GasPresentationEventKind kind)
        {
            return kind switch
            {
                GasPresentationEventKind.CastStarted => "Ability.CastStarted",
                GasPresentationEventKind.CastFailed => "Ability.CastFailed",
                GasPresentationEventKind.CastCommitted => "Ability.CastCommitted",
                GasPresentationEventKind.CastFinished => "Ability.CastFinished",
                GasPresentationEventKind.CastInterrupted => "Ability.CastInterrupted",
                GasPresentationEventKind.EffectApplied => "Effect.Applied",
                GasPresentationEventKind.EffectActivated => "Effect.Activated",
                GasPresentationEventKind.EffectExpired => "Effect.Expired",
                GasPresentationEventKind.EffectCancelled => "Effect.Cancelled",
                _ => null,
            };
        }

        private MapId ResolveMap(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<MapEntity>(entity))
            {
                return default;
            }

            return _world.Get<MapEntity>(entity).MapId;
        }
    }
}
