using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using ChampionSkillSandboxMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionSkillCommandSnapshotCaptureSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ChampionSkillSandboxRuntime _runtime;
        private readonly OrderQueue _orders;
        private readonly HashSet<Entity> _containers = new();

        public ChampionSkillCommandSnapshotCaptureSystem(
            GameEngine engine,
            ChampionSkillSandboxRuntime runtime,
            OrderQueue orders)
        {
            _engine = engine;
            _runtime = runtime;
            _orders = orders;
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

            _containers.Clear();
            _orders.CollectSelectionContainers(_containers);
            foreach (Entity container in _containers)
            {
                _runtime.CaptureCommandSnapshot(_engine, container);
            }
        }
    }
}
