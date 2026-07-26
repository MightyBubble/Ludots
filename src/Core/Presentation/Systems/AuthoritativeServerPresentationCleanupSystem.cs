using System;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class AuthoritativeServerPresentationCleanupSystem : BaseSystem<World, float>
    {
        private readonly GasPresentationEventBuffer _gasEvents;
        private readonly GlobalPresentationEventBuffer _globalEvents;
        private readonly PresentationEventStream _presentationEvents;
        private readonly PresentationOwnerChangeBuffer _ownerChanges;
        private readonly CommandBuffer _commandBuffer = new();
        private bool _enabled;

        public AuthoritativeServerPresentationCleanupSystem(
            World world,
            GasPresentationEventBuffer gasEvents,
            GlobalPresentationEventBuffer globalEvents,
            PresentationEventStream presentationEvents,
            PresentationOwnerChangeBuffer ownerChanges,
            bool enabled) : base(world)
        {
            _gasEvents = gasEvents ?? throw new ArgumentNullException(nameof(gasEvents));
            _globalEvents = globalEvents ?? throw new ArgumentNullException(nameof(globalEvents));
            _presentationEvents = presentationEvents ?? throw new ArgumentNullException(nameof(presentationEvents));
            _ownerChanges = ownerChanges ?? throw new ArgumentNullException(nameof(ownerChanges));
            _enabled = enabled;
        }

        internal void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public override void Update(in float dt)
        {
            if (!_enabled)
            {
                return;
            }

            _gasEvents.Clear();
            _globalEvents.Clear();
            _presentationEvents.Clear();
            _ownerChanges.Clear();
            ClearPresentationFlagsSystem.Clear(World, _commandBuffer);
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
