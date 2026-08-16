using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeIncomeSystem : BaseSystem<World, float>
    {
        private readonly DesertStrikeState _state;
        private readonly DesertStrikeConfig _config;
        private readonly IClock _clock;
        private readonly TagOps _tagOps;
        private readonly int _mineralsAttributeId;

        public DesertStrikeIncomeSystem(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config)
            : base(engine.World)
        {
            _state = state;
            _config = config;
            _clock = engine.GetService(CoreServiceKeys.Clock);
            _tagOps = engine.GetService(CoreServiceKeys.TagOps);
            _mineralsAttributeId = EnsureAttributeId("Minerals");
        }

        public override void Update(in float dt)
        {
            if (_state.GameOver)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.FixedFrame);
            if (step < _state.NextIncomeStep)
            {
                return;
            }

            _state.NextIncomeStep = step + _config.IncomeIntervalTicks;
            GrantMinerals(_state.PlayerBase);
            GrantMinerals(_state.AiBase);
        }

        private void GrantMinerals(Arch.Core.Entity baseEntity)
        {
            if (!World.IsAlive(baseEntity) || !World.Has<AttributeBuffer>(baseEntity))
            {
                return;
            }

            float current = World.Get<AttributeBuffer>(baseEntity).GetCurrent(_mineralsAttributeId);
            AttributeMutationOps.SetCurrent(World, baseEntity, _mineralsAttributeId, current + _config.IncomePerInterval, _tagOps);
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }
    }
}
