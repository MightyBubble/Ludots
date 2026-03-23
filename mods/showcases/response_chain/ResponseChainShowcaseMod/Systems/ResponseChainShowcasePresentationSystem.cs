using Arch.System;
using Ludots.Core.Engine;
using ResponseChainShowcaseMod.Runtime;

namespace ResponseChainShowcaseMod.Systems
{
    internal sealed class ResponseChainShowcasePresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ResponseChainShowcaseRuntime _runtime;

        public ResponseChainShowcasePresentationSystem(GameEngine engine, ResponseChainShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _runtime.Update(_engine);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
