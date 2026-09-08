using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Physics2D;
using Ludots.Core.Scripting;

namespace Ludots.Core.Movement.Physics2DBridge
{
    /// <summary>
    /// Sensor-emission tap (#1469): routes contact begin/end edges whose party
    /// carries <see cref="RegionVolumeEmissionCm"/> into the unified region
    /// emission contract — the same outlet, vocabulary, and schema validation the
    /// vector volume and field membership lines use. Contact semantics stay
    /// physical (kinematic×static sensor pairing, per-step edges); only the event
    /// outlet converges. The counterpart entity rides MapTrigger.SourceEntity;
    /// parties without a map scope (off-map physics entities) are skipped.
    /// </summary>
    public sealed class ContactEmissionTap
    {
        private readonly World _world;
        private readonly TriggerManager _triggerManager;
        private readonly Func<MapSessionManager?> _sessions;
        private readonly Func<ScriptContext> _contextFactory;

        public ContactEmissionTap(
            World world,
            TriggerManager triggerManager,
            Func<MapSessionManager?> sessions,
            Func<ScriptContext> contextFactory)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public void Process(ReadOnlySpan<ContactEvent2D> events)
        {
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly ContactEvent2D contactEvent = ref events[i];
                FireIfEmitter(in contactEvent, emitter: contactEvent.EntityA, counterpart: contactEvent.EntityB);
                FireIfEmitter(in contactEvent, emitter: contactEvent.EntityB, counterpart: contactEvent.EntityA);
            }
        }

        private void FireIfEmitter(in ContactEvent2D contactEvent, Entity emitter, Entity counterpart)
        {
            if (!_world.IsAlive(emitter) || !_world.IsAlive(counterpart))
            {
                return;
            }

            if (!_world.TryGet(emitter, out RegionVolumeEmissionCm emission))
            {
                return;
            }

            if (!_world.TryGet(emitter, out MapEntity mapBinding))
            {
                return;
            }

            MapSession? session = _sessions()?.GetSession(mapBinding.MapId);
            if (session == null || session.State != MapSessionState.Active)
            {
                return;
            }

            EventKey eventKey = contactEvent.Type == ContactEventType2D.Begin
                ? emission.EnterEvent
                : emission.ExitEvent;
            EventSchema? schema = _triggerManager.EventSchemas != null &&
                _triggerManager.EventSchemas.TryGet(eventKey.Value, out EventSchema resolved)
                    ? resolved
                    : null;
            RegionEmissionFiring.Fire(
                _triggerManager,
                _contextFactory,
                session,
                eventKey,
                regionId: string.Empty,
                counterpart,
                schema,
                emission.Payload);
        }
    }
}
