using Arch.System;
using Ludots.Core.Engine;
using CapabilityStandardStaticPerformer30kMod.Runtime;

namespace CapabilityStandardStaticPerformer30kMod.Systems
{
    internal sealed class CapabilityStandardStaticPerformer30kPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly CapabilityStandardStaticPerformer30kRuntime _runtime;

        public CapabilityStandardStaticPerformer30kPresentationSystem(
            GameEngine engine,
            CapabilityStandardStaticPerformer30kRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            _runtime.Update(_engine);
        }
    }
}
