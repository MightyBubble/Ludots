using Arch.System;
using InteractionShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace InteractionShowcaseMod.Systems
{
    internal sealed class InteractionShowcasePanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly InteractionShowcaseRuntime _runtime;

        public InteractionShowcasePanelPresentationSystem(GameEngine engine, InteractionShowcaseRuntime runtime)
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
            _runtime.RefreshPanel(_engine);
        }
    }
}
