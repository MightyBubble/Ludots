using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace Ludots.Core.ParticipantVisibility
{
    public sealed class DynamicParticipantVisibilitySystem : BaseSystem<World, float>
    {
        private readonly Func<DynamicParticipantVisibilityPublisher?> _publisherAccessor;
        private readonly Func<int>? _currentTickAccessor;
        private int _fallbackTick;

        public DynamicParticipantVisibilitySystem(
            World world,
            DynamicParticipantVisibilityPublisher publisher)
            : this(world, () => publisher, currentTickAccessor: null)
        {
            ArgumentNullException.ThrowIfNull(publisher);
        }

        public DynamicParticipantVisibilitySystem(
            World world,
            Func<DynamicParticipantVisibilityPublisher?> publisherAccessor)
            : this(world, publisherAccessor, currentTickAccessor: null)
        {
        }

        public DynamicParticipantVisibilitySystem(
            World world,
            Func<DynamicParticipantVisibilityPublisher?> publisherAccessor,
            IClock clock)
            : this(world, publisherAccessor, () => clock.Now(ClockDomainId.Step))
        {
            ArgumentNullException.ThrowIfNull(clock);
        }

        public DynamicParticipantVisibilitySystem(
            World world,
            Func<DynamicParticipantVisibilityPublisher?> publisherAccessor,
            Func<int>? currentTickAccessor)
            : base(world)
        {
            _publisherAccessor = publisherAccessor ?? throw new ArgumentNullException(nameof(publisherAccessor));
            _currentTickAccessor = currentTickAccessor;
        }

        public override void Update(in float dt)
        {
            int currentTick = _currentTickAccessor == null
                ? ++_fallbackTick
                : _currentTickAccessor();
            _publisherAccessor()?.Publish(currentTick);
        }
    }
}
