using Arch.Core;
using Arch.System;
using ChampionSkillSandboxMod.Runtime;
using Ludots.Core.Engine;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionSkillCommandSnapshotCaptureSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ChampionSkillSandboxRuntime _runtime;

        public ChampionSkillCommandSnapshotCaptureSystem(
            GameEngine engine,
            ChampionSkillSandboxRuntime runtime)
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
            if (!ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            _runtime.CaptureCommandSnapshot(_engine);
        }
    }
}
