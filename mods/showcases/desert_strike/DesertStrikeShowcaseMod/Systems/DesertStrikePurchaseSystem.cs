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
    public sealed class DesertStrikePurchaseSystem : BaseSystem<World, float>
    {
        private readonly DesertStrikeState _state;
        private readonly DesertStrikeConfig _config;
        private readonly TagOps _tagOps;
        private readonly int _mineralsAttributeId;

        public DesertStrikePurchaseSystem(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config)
            : base(engine.World)
        {
            _state = state;
            _config = config;
            _tagOps = engine.GetService(CoreServiceKeys.TagOps);
            _mineralsAttributeId = EnsureAttributeId("Minerals");
        }

        public override void Update(in float dt)
        {
            if (_state.GameOver)
            {
                StripBuyTags(_state.PlayerBase);
                StripBuyTags(_state.AiBase);
                return;
            }

            ProcessBase(_state.PlayerBase, player: true);
            ProcessBase(_state.AiBase, player: false);
        }

        private void ProcessBase(Arch.Core.Entity shop, bool player)
        {
            if (!World.IsAlive(shop) ||
                !World.Has<GameplayTagContainer>(shop) ||
                !World.Has<AttributeBuffer>(shop))
            {
                return;
            }

            var queue = player ? _state.PlayerQueue : _state.AiQueue;
            foreach (var pair in _config.Units)
            {
                string unitId = pair.Key;
                DesertStrikeConfig.UnitConfig unit = pair.Value;
                int tagId = TagRegistry.GetId(unit.PurchaseTag);
                if (tagId <= 0 || !World.Get<GameplayTagContainer>(shop).HasTag(tagId))
                {
                    continue;
                }

                _tagOps.RemoveTag(World, shop, tagId);
                float minerals = World.Get<AttributeBuffer>(shop).GetCurrent(_mineralsAttributeId);
                if (minerals < unit.Cost)
                {
                    _state.PurchaseDeniedCount++;
                    continue;
                }

                AttributeMutationOps.SetCurrent(World, shop, _mineralsAttributeId, minerals - unit.Cost, _tagOps);
                int laneCount = player ? _state.PlayerSpawnMarkers.Count : _state.AiSpawnMarkers.Count;
                int lane = player ? _state.PlayerNextLane : _state.AiNextLane;
                queue.Add(new DesertStrikePurchase(unitId, lane));
                int nextLane = laneCount > 0 ? (lane + 1) % laneCount : 0;
                if (player)
                {
                    _state.PlayerNextLane = nextLane;
                }
                else
                {
                    _state.AiNextLane = nextLane;
                }
            }
        }

        private void StripBuyTags(Arch.Core.Entity shop)
        {
            if (!World.IsAlive(shop) || !World.Has<GameplayTagContainer>(shop))
            {
                return;
            }

            foreach (var unit in _config.Units.Values)
            {
                int tagId = TagRegistry.GetId(unit.PurchaseTag);
                if (tagId > 0 && World.Get<GameplayTagContainer>(shop).HasTag(tagId))
                {
                    _tagOps.RemoveTag(World, shop, tagId);
                }
            }
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }
    }
}
