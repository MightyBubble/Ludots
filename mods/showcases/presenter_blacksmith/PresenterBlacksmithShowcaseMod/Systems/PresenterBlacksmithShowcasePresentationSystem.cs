using Arch.System;
using Ludots.Core.Engine;
using PresenterBlacksmithShowcaseMod.Runtime;

namespace PresenterBlacksmithShowcaseMod.Systems
{
    internal sealed class PresenterBlacksmithShowcasePresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly PresenterBlacksmithShowcaseRuntime _runtime;

        public PresenterBlacksmithShowcasePresentationSystem(
            GameEngine engine,
            PresenterBlacksmithShowcaseRuntime runtime)
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
