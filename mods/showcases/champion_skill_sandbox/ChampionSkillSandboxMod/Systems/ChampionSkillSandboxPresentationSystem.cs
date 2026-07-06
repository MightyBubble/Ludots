using Arch.System;
using ChampionSkillSandboxMod.Runtime;
using Ludots.Core.Engine;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionSkillSandboxPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ChampionSkillSandboxRuntime _runtime;

        public ChampionSkillSandboxPresentationSystem(GameEngine engine, ChampionSkillSandboxRuntime runtime)
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
