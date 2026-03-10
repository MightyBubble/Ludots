using Arch.System;
using CameraShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace CameraShowcaseMod.Systems
{
    internal sealed class CameraShowcasePanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly CameraShowcaseRuntime _runtime;

        public CameraShowcasePanelPresentationSystem(GameEngine engine, CameraShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            _runtime.RefreshPanel(_engine);
        }
    }
}
