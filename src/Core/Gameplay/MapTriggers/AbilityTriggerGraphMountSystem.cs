using Arch.System;
using Ludots.Core.Gameplay.GAS.Presentation;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Drives ability-domain TriggerGraph mounts from the GAS presentation event
    /// buffer. Runs in AbilityActivation right after AbilityExecSystem (which wrote
    /// this tick's cast moments), so mounts are created on the same tick a cast
    /// starts — before TriggerGraphMomentBridgeSystem fires Ability.* into the map
    /// bus in ClearPresentationFlags — and torn down on the tick a terminal moment
    /// lands. Read-only for the buffer (the presentation projection still consumes
    /// every moment exactly once).
    /// </summary>
    public sealed class AbilityTriggerGraphMountSystem : ISystem<float>
    {
        private readonly AbilityTriggerGraphMounts _mounts;
        private readonly GasPresentationEventBuffer _gasEvents;

        public AbilityTriggerGraphMountSystem(
            AbilityTriggerGraphMounts mounts,
            GasPresentationEventBuffer gasEvents)
        {
            _mounts = mounts ?? throw new ArgumentNullException(nameof(mounts));
            _gasEvents = gasEvents ?? throw new ArgumentNullException(nameof(gasEvents));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            _mounts.ProcessPresentationEvents(_gasEvents.Events);
        }
    }
}
