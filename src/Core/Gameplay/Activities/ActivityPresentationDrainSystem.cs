using System;
using Arch.System;

namespace Ludots.Core.Gameplay.Activities
{
    /// <summary>
    /// Clears the activity presentation/lifecycle buffers once per fixed step so they
    /// behave as a same-frame window (mirrors GasPresentationEventBuffer consumption in
    /// this phase). Consumers — DataPlane topic producers, acceptance writers — read the
    /// window earlier in the step; anything unread is intentionally dropped, never
    /// accumulated across frames.
    /// </summary>
    public sealed class ActivityPresentationDrainSystem : ISystem<float>
    {
        private readonly ActivityPresentationBuffer _presentation;
        private readonly ActivityLifecycleBuffer _lifecycle;

        public ActivityPresentationDrainSystem(
            ActivityPresentationBuffer presentation,
            ActivityLifecycleBuffer lifecycle)
        {
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            _presentation.Clear();
            _lifecycle.Clear();
        }
    }
}
