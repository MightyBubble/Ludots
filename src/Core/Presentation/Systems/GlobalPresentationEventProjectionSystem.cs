using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class GlobalPresentationEventProjectionSystem : BaseSystem<World, float>
    {
        private readonly GlobalPresentationEventBuffer _globalEvents;
        private readonly PresentationEventStream _stream;
        private readonly GameSession _session;

        public GlobalPresentationEventProjectionSystem(
            World world,
            GlobalPresentationEventBuffer globalEvents,
            PresentationEventStream stream,
            GameSession session)
            : base(world)
        {
            _globalEvents = globalEvents ?? throw new ArgumentNullException(nameof(globalEvents));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public override void Update(in float dt)
        {
            ReadOnlySpan<GlobalPresentationEvent> span = _globalEvents.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly GlobalPresentationEvent evt = ref span[i];
                if (!_stream.TryAdd(new PresentationEvent
                    {
                        LogicTickStamp = _session.CurrentTick,
                        Kind = evt.Kind,
                        KeyId = evt.KeyId,
                        Source = evt.Source,
                        Target = evt.Target,
                        Magnitude = evt.Magnitude,
                        PayloadA = evt.PayloadA,
                        PayloadB = evt.PayloadB,
                    }))
                {
                    throw new InvalidOperationException($"PresentationEventStream is full while projecting global event kind={evt.Kind}, keyId={evt.KeyId}.");
                }
            }

            _globalEvents.Clear();
        }
    }
}
