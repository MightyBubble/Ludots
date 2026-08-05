using Arch.System;
using Ludots.Core.Engine;
using PerformerBlacksmithShowcaseMod.Runtime;

namespace PerformerBlacksmithShowcaseMod.Systems
{
    internal sealed class PerformerBlacksmithShowcaseKnowledgeProjectionSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly PerformerBlacksmithShowcaseRuntime _runtime;

        public PerformerBlacksmithShowcaseKnowledgeProjectionSystem(
            GameEngine engine,
            PerformerBlacksmithShowcaseRuntime runtime)
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
