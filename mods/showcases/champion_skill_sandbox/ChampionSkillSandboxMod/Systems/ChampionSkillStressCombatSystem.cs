using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using ChampionSkillSandboxMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Components;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionSkillStressCombatSystem : ISystem<float>
    {
        private static readonly QueryDescription StressUnitQuery = new QueryDescription()
            .WithAll<Name, Team, MapEntity, AbilityStateBuffer, WorldPositionCm, AttributeBuffer, OrderBuffer>();

        private static readonly QueryDescription ProjectileQuery = new QueryDescription()
            .WithAll<ProjectileState>();

        private readonly GameEngine _engine;
        private readonly OrderQueue _orders;
        private readonly CompositeOrderPlanner _planner;
        private readonly ChampionSkillStressTelemetry _telemetry;
        private readonly int _castAbilityOrderTypeId;
        private readonly int _healthAttributeId;
        private readonly List<StressUnitState> _teamA = new();
        private readonly List<StressUnitState> _teamB = new();
        private int _tick;

        public ChampionSkillStressCombatSystem(
            GameEngine engine,
            OrderQueue orders,
            ChampionSkillStressTelemetry telemetry)
        {
            _engine = engine;
            _orders = orders;
            _telemetry = telemetry;

            GameConfig config = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.GameConfig)
                ?? throw new InvalidOperationException("ChampionSkillStressCombatSystem requires GameConfig.");
            AbilityDefinitionRegistry abilities = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("ChampionSkillStressCombatSystem requires AbilityDefinitionRegistry.");

            _castAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
            _planner = new CompositeOrderPlanner(
                engine.World,
                orders,
                abilities,
                config.Constants.OrderTypeIds["castAbility"],
                config.Constants.OrderTypeIds["moveTo"]);
            _healthAttributeId = AttributeRegistry.GetId("Health");
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value))
            {
                _telemetry.Reset();
                _tick = 0;
                return;
            }

            _tick++;
            CollectUnits();

            int ordersIssued = 0;
            ordersIssued += IssueOrders(_teamA, _teamB, playerId: 1);
            ordersIssued += IssueOrders(_teamB, _teamA, playerId: 2);

            int projectileCount = CountProjectiles();
            _telemetry.IsActive = true;
            _telemetry.LiveTeamA = _teamA.Count;
            _telemetry.LiveTeamB = _teamB.Count;
            _telemetry.ProjectileCount = projectileCount;
            _telemetry.QueueDepth = _orders.Count;
            _telemetry.OrdersIssued += ordersIssued;
            if (projectileCount > _telemetry.PeakProjectileCount)
            {
                _telemetry.PeakProjectileCount = projectileCount;
            }
        }

        private void CollectUnits()
        {
            _teamA.Clear();
            _teamB.Clear();
            string mapId = _engine.CurrentMapSession!.MapId.Value;
            _engine.World.Query(in StressUnitQuery, (Entity entity, ref Name name, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer _, ref WorldPositionCm position, ref AttributeBuffer attributes, ref OrderBuffer orders) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, mapId, StringComparison.Ordinal))
                {
                    return;
                }

                var state = new StressUnitState(
                    entity,
                    ResolveRole(name.Value),
                    new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat()),
                    ReadHealthRatio(attributes),
                    orders.HasActive || orders.HasPending || orders.HasQueued);

                if (team.Id == 1)
                {
                    _teamA.Add(state);
                }
                else if (team.Id == 2)
                {
                    _teamB.Add(state);
                }
            });
        }

        private int IssueOrders(List<StressUnitState> actors, List<StressUnitState> targets, int playerId)
        {
            if (actors.Count == 0 || targets.Count == 0)
            {
                return 0;
            }

            int issued = 0;
            for (int i = 0; i < actors.Count; i++)
            {
                StressUnitState actor = actors[i];
                if (actor.HasOutstandingOrder || !ShouldAttempt(actor.Role, actor.Entity.Id))
                {
                    continue;
                }

                Entity target = actor.Role == StressRole.Priest
                    ? ResolveHealTarget(actors)
                    : ResolveNearestTarget(actor.PositionCm, targets);
                if (target == Entity.Null)
                {
                    continue;
                }

                var order = new Order
                {
                    OrderTypeId = _castAbilityOrderTypeId,
                    PlayerId = playerId,
                    Actor = actor.Entity,
                    Target = target,
                    SubmitMode = OrderSubmitMode.Immediate,
                    Args = new OrderArgs
                    {
                        I0 = 0,
                    }
                };

                OrderSubmitResult submitResult = _planner.Submit(in order);
                if (submitResult == OrderSubmitResult.Queued ||
                    submitResult == OrderSubmitResult.Activated ||
                    submitResult == OrderSubmitResult.Pending)
                {
                    issued++;
                }
            }

            return issued;
        }

        private bool ShouldAttempt(StressRole role, int entityId)
        {
            int cadence = role switch
            {
                StressRole.Warrior => 14,
                StressRole.FireMage => 20,
                StressRole.LaserMage => 16,
                _ => 18,
            };

            return ((_tick + entityId) % cadence) == 0;
        }

        private Entity ResolveHealTarget(List<StressUnitState> allies)
        {
            float lowestRatio = 0.98f;
            Entity best = Entity.Null;
            for (int i = 0; i < allies.Count; i++)
            {
                StressUnitState ally = allies[i];
                if (ally.HealthRatio < lowestRatio)
                {
                    lowestRatio = ally.HealthRatio;
                    best = ally.Entity;
                }
            }

            return best;
        }

        private static Entity ResolveNearestTarget(Vector2 actorPositionCm, List<StressUnitState> targets)
        {
            float bestDistanceSq = float.MaxValue;
            Entity best = Entity.Null;
            for (int i = 0; i < targets.Count; i++)
            {
                StressUnitState target = targets[i];
                float distanceSq = Vector2.DistanceSquared(actorPositionCm, target.PositionCm);
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    best = target.Entity;
                }
            }

            return best;
        }

        private int CountProjectiles()
        {
            int count = 0;
            _engine.World.Query(in ProjectileQuery, (Entity _, ref ProjectileState __) => count++);
            return count;
        }

        private float ReadHealthRatio(in AttributeBuffer attributes)
        {
            if (_healthAttributeId < 0)
            {
                return 1f;
            }

            float current = attributes.GetCurrent(_healthAttributeId);
            float max = attributes.GetBase(_healthAttributeId);
            if (max <= 0f)
            {
                return 1f;
            }

            return current / max;
        }

        private static StressRole ResolveRole(string name)
        {
            if (name.Contains("FireMage", StringComparison.Ordinal))
            {
                return StressRole.FireMage;
            }

            if (name.Contains("LaserMage", StringComparison.Ordinal))
            {
                return StressRole.LaserMage;
            }

            if (name.Contains("Priest", StringComparison.Ordinal))
            {
                return StressRole.Priest;
            }

            return StressRole.Warrior;
        }

        private readonly record struct StressUnitState(
            Entity Entity,
            StressRole Role,
            Vector2 PositionCm,
            float HealthRatio,
            bool HasOutstandingOrder);

        private enum StressRole
        {
            Warrior,
            FireMage,
            LaserMage,
            Priest,
        }
    }
}
