using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using ChampionSkillSandboxMod.Runtime;

namespace ChampionSkillSandboxMod.Systems
{
    /// <summary>
    /// Polls the sandbox-bound cast-mode keys (ChampionSkillSandbox.Controls context) and applies
    /// the picked cast mode. Runs in LocalInput so replicated clients (which execute only that
    /// group) keep the mode keys alive; sandbox-map scoping comes from the Controls context only
    /// being pushed while the sandbox map is focused.
    /// </summary>
    internal sealed class ChampionSkillCastModePollSystem : ISystem<float>
    {
        private readonly GameEngine _engine;

        public ChampionSkillCastModePollSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            ChampionSkillSandboxRuntime.PollCastModeSwitch(_engine);
        }
    }
}
