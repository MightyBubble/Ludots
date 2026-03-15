using Arch.System;
using EntityCommandPanelShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace EntityCommandPanelShowcaseMod.Systems
{
    internal sealed class EntityCommandPanelShowcasePresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly EntityCommandPanelShowcaseRuntime _runtime;

        public EntityCommandPanelShowcasePresentationSystem(GameEngine engine, EntityCommandPanelShowcaseRuntime runtime)
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
