using Arch.System;
using DialogueAuthorKitShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace DialogueAuthorKitShowcaseMod.Systems
{
    internal sealed class DialogueAuthorKitPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly DialogueAuthorKitRuntime _runtime;

        internal DialogueAuthorKitPresentationSystem(GameEngine engine, DialogueAuthorKitRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt) => _runtime.RefreshPanel(_engine);
    }
}
