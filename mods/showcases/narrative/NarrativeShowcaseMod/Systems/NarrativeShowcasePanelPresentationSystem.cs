using Arch.System;
using Ludots.Core.Engine;
using NarrativeShowcaseMod.Runtime;

namespace NarrativeShowcaseMod.Systems
{
    internal sealed class NarrativeShowcasePanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NarrativeShowcaseRuntime _runtime;

        internal NarrativeShowcasePanelPresentationSystem(GameEngine engine, NarrativeShowcaseRuntime runtime)
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
