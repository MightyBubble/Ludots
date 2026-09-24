using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Advances presenter-local named timers on the render clock and publishes
    /// <see cref="PresentationEventKind.TimerExpired"/> into the shared event stream.
    /// Runs before <see cref="PresenterRuleSystem"/> so an expiry is consumable by rules
    /// in the same frame; TimerSet/TimerKill commands are applied by
    /// <see cref="PresenterRuntimeSystem"/> when it drains the command buffer.
    /// </summary>
    public sealed class PresenterTimerSystem : BaseSystem<World, float>
    {
        private readonly PresenterTimerTable _timers;
        private readonly PresentationEventStream _events;

        public PresenterTimerSystem(World world, PresenterTimerTable timers, PresentationEventStream events)
            : base(world)
        {
            _timers = timers ?? throw new ArgumentNullException(nameof(timers));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public override void Update(in float dt)
        {
            if (dt < 0f || !float.IsFinite(dt))
            {
                throw new InvalidOperationException($"PresenterTimerSystem dt must be finite and >= 0, got {dt}.");
            }

            if (_timers.Tick(dt) == 0)
            {
                return;
            }

            for (int i = 0; i < _timers.ExpiredCount; i++)
            {
                if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.TimerExpired,
                    KeyId = _timers.GetExpiredNameId(i),
                    Source = _timers.GetExpiredOwner(i),
                    Target = _timers.GetExpiredOwner(i),
                    PresenterEntity = _timers.GetExpiredPresenter(i),
                    Magnitude = _timers.GetExpiredStableId(i),
                }))
                {
                    throw new InvalidOperationException("PresentationEventStream is full while publishing TimerExpired.");
                }
            }
        }
    }
}
