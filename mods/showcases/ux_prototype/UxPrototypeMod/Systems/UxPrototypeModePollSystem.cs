using Arch.System;
using Ludots.Core.Engine;
using UxPrototypeMod.Runtime;

namespace UxPrototypeMod.Systems
{
    /// <summary>
    /// Polls the prototype-bound mode keys (UxPrototype.Controls context, gated by the runtime's
    /// prototype-map scope) and applies the picked scenario mode. Runs in LocalInput so replicated
    /// clients (which execute only that group) keep the mode keys alive.
    /// </summary>
    internal sealed class UxPrototypeModePollSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly UxPrototypeRuntime _runtime;

        public UxPrototypeModePollSystem(GameEngine engine, UxPrototypeRuntime runtime)
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
            _runtime.PollModeSwitch(_engine);
        }
    }
}
