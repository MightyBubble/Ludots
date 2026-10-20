using System;
using Arch.System;
using EntityCommandPanelMod.Runtime;

namespace EntityCommandPanelMod.Systems
{
    public sealed class EntityCommandPanelActivationDispatchSystem : ISystem<float>
    {
        private readonly EntityCommandPanelSlotActivations _activations;

        public EntityCommandPanelActivationDispatchSystem(EntityCommandPanelSlotActivations activations)
        {
            _activations = activations ?? throw new ArgumentNullException(nameof(activations));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            _activations.Dispatch();
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
