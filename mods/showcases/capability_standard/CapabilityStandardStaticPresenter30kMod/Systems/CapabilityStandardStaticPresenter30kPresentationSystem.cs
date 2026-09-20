using Arch.System;
using Ludots.Core.Engine;
using CapabilityStandardStaticPresenter30kMod.Runtime;

namespace CapabilityStandardStaticPresenter30kMod.Systems
{
    internal sealed class CapabilityStandardStaticPresenter30kPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly CapabilityStandardStaticPresenter30kRuntime _runtime;

        public CapabilityStandardStaticPresenter30kPresentationSystem(
            GameEngine engine,
            CapabilityStandardStaticPresenter30kRuntime runtime)
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
