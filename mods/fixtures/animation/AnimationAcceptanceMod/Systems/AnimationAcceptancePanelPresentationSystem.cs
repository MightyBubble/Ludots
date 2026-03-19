using AnimationAcceptanceMod.Runtime;
using Arch.System;
using Ludots.Core.Engine;

namespace AnimationAcceptanceMod.Systems
{
    internal sealed class AnimationAcceptancePanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly AnimationAcceptanceRuntime _runtime;

        public AnimationAcceptancePanelPresentationSystem(GameEngine engine, AnimationAcceptanceRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void Update(in float t)
        {
            _runtime.RefreshPanel(_engine);
        }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }
    }
}
