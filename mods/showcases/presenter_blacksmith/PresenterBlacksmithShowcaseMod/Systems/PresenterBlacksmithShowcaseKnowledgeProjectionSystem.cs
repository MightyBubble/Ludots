using Arch.System;
using Ludots.Core.Engine;
using PresenterBlacksmithShowcaseMod.Runtime;

namespace PresenterBlacksmithShowcaseMod.Systems
{
    internal sealed class PresenterBlacksmithShowcaseKnowledgeProjectionSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly PresenterBlacksmithShowcaseRuntime _runtime;

        public PresenterBlacksmithShowcaseKnowledgeProjectionSystem(
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
            _runtime.UpdateKnowledgeProjection(_engine);
        }
    }
}
