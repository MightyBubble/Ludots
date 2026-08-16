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
    public sealed class DesertStrikeAiPlayerSystem : BaseSystem<World, float>
    {
        private const int MaxPurchasesPerThink = 4;

        private readonly DesertStrikeState _state;
        private readonly DesertStrikeConfig _config;
        private readonly IClock _clock;
        private readonly TagOps _tagOps;
        private readonly int _mineralsAttributeId;
        private int _nextThinkStep;

        public DesertStrikeAiPlayerSystem(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config)
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

            if (!World.IsAlive(_state.AiBase) || !World.Has<AttributeBuffer>(_state.AiBase))
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.FixedFrame);
            if (_nextThinkStep == 0)
            {
                _nextThinkStep = step + _config.Ai.ThinkIntervalTicks;
                return;
            }

            if (step < _nextThinkStep)
            {
                return;
            }

            _nextThinkStep = step + _config.Ai.ThinkIntervalTicks;
            SpendBudget(step);
        }

        private void SpendBudget(int step)
        {
            var wallet = World.Get<AttributeBuffer>(_state.AiBase);
            for (int purchase = 0; purchase < MaxPurchasesPerThink; purchase++)
            {
                float minerals = wallet.GetCurrent(_mineralsAttributeId);
                string? unitId = PickAffordableUnit(minerals, step + purchase);
                if (unitId == null)
                {
                    return;
                }

                DesertStrikeConfig.UnitConfig unit = _config.Units[unitId];
                AttributeMutationOps.SetCurrent(World, _state.AiBase, _mineralsAttributeId, minerals - unit.Cost, _tagOps);
                int laneCount = Math.Max(1, _state.AiSpawnMarkers.Count);
                int lane = _state.AiNextLane;
                _state.AiNextLane = (lane + 1) % laneCount;
                _state.AiQueue.Add(new DesertStrikePurchase(unitId, lane));
            }
        }

        private string? PickAffordableUnit(float minerals, int seed)
        {
            int totalWeight = 0;
            foreach (var pair in _config.Ai.Weights)
            {
                if (!_config.Units.TryGetValue(pair.Key, out DesertStrikeConfig.UnitConfig? unit))
                {
                    throw new InvalidOperationException($"DS.AI.ERR.UnknownWeightUnit: unitId={pair.Key}.");
                }

                if (unit.Cost <= minerals)
                {
                    totalWeight += pair.Value;
                }
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            int pick = ((seed * 31 + 7) % totalWeight + totalWeight) % totalWeight;
            int accumulated = 0;
            foreach (var pair in _config.Ai.Weights)
            {
                DesertStrikeConfig.UnitConfig unit = _config.Units[pair.Key];
                if (unit.Cost > minerals)
                {
                    continue;
                }

                accumulated += pair.Value;
                if (pick < accumulated)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }
    }
}
