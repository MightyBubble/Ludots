using Arch.System;
using Ludots.Core.Engine;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundPanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly MassFlowNavPlaygroundRuntime _runtime;

        public MassFlowNavPlaygroundPanelPresentationSystem(GameEngine engine, MassFlowNavPlaygroundRuntime runtime)
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
            _runtime.RefreshPanel(_engine, t);
        }
    }
}
