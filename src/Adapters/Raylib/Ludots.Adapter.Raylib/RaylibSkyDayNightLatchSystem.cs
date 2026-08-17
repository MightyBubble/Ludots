using System;
using Arch.Core;
using Arch.System;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Events;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibSkyDayNightLatchSystem : BaseSystem<World, float>
    {
        private readonly GlobalPresentationEventBuffer _globalEvents;
        private readonly RaylibSkyEnvironment _skyEnvironment;

        public RaylibSkyDayNightLatchSystem(
            World world,
            GlobalPresentationEventBuffer globalEvents,
            RaylibSkyEnvironment skyEnvironment)
            : base(world)
        {
            _globalEvents = globalEvents ?? throw new ArgumentNullException(nameof(globalEvents));
            _skyEnvironment = skyEnvironment ?? throw new ArgumentNullException(nameof(skyEnvironment));
        }

        public override void Update(in float dt)
        {
            _skyEnvironment.ApplyDayNightEvents(_globalEvents.GetSpan());
        }
    }
}
