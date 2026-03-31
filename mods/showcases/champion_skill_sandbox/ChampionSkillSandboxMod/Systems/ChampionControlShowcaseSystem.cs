using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionControlShowcaseSystem : ISystem<float>
    {
        private static readonly QueryDescription ControlQuery = new QueryDescription()
            .WithAll<Name, MapEntity, WorldPositionCm, OrderBuffer>();

        private static readonly Vector3 RunnerLeftPoint = new(2180f, 0f, 1060f);
        private static readonly Vector3 RunnerRightPoint = new(3140f, 0f, 1060f);

        private readonly GameEngine _engine;
        private readonly OrderQueue _orders;
        private readonly CompositeOrderPlanner _planner;
        private readonly int _moveToOrderTypeId;
        private readonly int _castAbilityOrderTypeId;
        private int _tick;

        public ChampionControlShowcaseSystem(GameEngine engine, OrderQueue orders)
        {
            _engine = engine;
            _orders = orders;

            var gameConfig = engine.GetService(CoreServiceKeys.GameConfig)
                ?? throw new InvalidOperationException("ChampionControlShowcaseSystem requires GameConfig.");
            var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("ChampionControlShowcaseSystem requires AbilityDefinitionRegistry.");

            _moveToOrderTypeId = gameConfig.Constants.OrderTypeIds["moveTo"];
            _castAbilityOrderTypeId = gameConfig.Constants.OrderTypeIds["castAbility"];
            _planner = new CompositeOrderPlanner(_engine.World, _orders, abilities, _castAbilityOrderTypeId, _moveToOrderTypeId);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!ChampionSkillSandboxIds.IsControlMap(_engine.CurrentMapSession?.MapId.Value))
            {
                _tick = 0;
                return;
            }

            _tick++;

            Entity hero = Entity.Null;
            Entity runner = Entity.Null;
            Entity caster = Entity.Null;
            Vector2 runnerPosition = default;
            bool runnerHasOrder = false;
            bool casterHasOrder = false;

            string mapId = _engine.CurrentMapSession!.MapId.Value;
            _engine.World.Query(in ControlQuery, (Entity entity, ref Name name, ref MapEntity mapEntity, ref WorldPositionCm position, ref OrderBuffer orders) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (string.Equals(name.Value, ChampionSkillSandboxIds.ControlHeroName, StringComparison.OrdinalIgnoreCase))
                {
                    hero = entity;
                    return;
                }

                if (string.Equals(name.Value, ChampionSkillSandboxIds.ControlRunnerName, StringComparison.OrdinalIgnoreCase))
                {
                    runner = entity;
                    runnerPosition = new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
                    runnerHasOrder = HasOutstandingOrder(entity, in orders);
                    return;
                }

                if (string.Equals(name.Value, ChampionSkillSandboxIds.ControlCasterName, StringComparison.OrdinalIgnoreCase))
                {
                    caster = entity;
                    casterHasOrder = HasOutstandingOrder(entity, in orders);
                }
            });

            if (runner != Entity.Null && !runnerHasOrder)
            {
                SubmitRunnerMove(runner, runnerPosition);
            }

            if (hero != Entity.Null &&
                caster != Entity.Null &&
                !casterHasOrder &&
                !_engine.World.Has<AbilityExecInstance>(caster) &&
                ((_tick + caster.Id) % 72) == 0)
            {
                SubmitCasterPulse(caster, hero);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void SubmitRunnerMove(Entity runner, Vector2 currentPositionCm)
        {
            Vector2 left = new(RunnerLeftPoint.X, RunnerLeftPoint.Z);
            Vector2 right = new(RunnerRightPoint.X, RunnerRightPoint.Z);
            float distanceToLeft = Vector2.Distance(currentPositionCm, left);
            float distanceToRight = Vector2.Distance(currentPositionCm, right);

            Vector3 target = distanceToLeft <= 80f
                ? RunnerRightPoint
                : distanceToRight <= 80f
                    ? RunnerLeftPoint
                    : currentPositionCm.X <= ((left.X + right.X) * 0.5f)
                        ? RunnerRightPoint
                        : RunnerLeftPoint;

            var order = new Order
            {
                OrderTypeId = _moveToOrderTypeId,
                PlayerId = 2,
                Actor = runner,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = target
                    }
                }
            };

            _orders.TryEnqueueAssigned(ref order);
        }

        private void SubmitCasterPulse(Entity caster, Entity hero)
        {
            var order = new Order
            {
                OrderTypeId = _castAbilityOrderTypeId,
                PlayerId = 2,
                Actor = caster,
                Target = hero,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    I0 = 0
                }
            };

            _planner.TrySubmit(in order);
        }

        private bool HasOutstandingOrder(Entity entity, in OrderBuffer orders)
        {
            return orders.HasActive ||
                   orders.HasPending ||
                   orders.HasQueued ||
                   _engine.World.Has<AbilityExecInstance>(entity);
        }
    }
}
