using Arch.System;
using Ludots.Core.Engine;
using Navigation2DPlaygroundMod.Runtime;

namespace Navigation2DPlaygroundMod.Systems
{
    internal sealed class Navigation2DPlaygroundPanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly Navigation2DPlaygroundRuntime _runtime;

        public Navigation2DPlaygroundPanelPresentationSystem(GameEngine engine, Navigation2DPlaygroundRuntime runtime)
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
