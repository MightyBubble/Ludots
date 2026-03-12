using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using InteractionShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace InteractionShowcaseMod.Systems
{
    internal sealed class InteractionShowcaseStressSystem : ISystem<float>
    {
        private const int DesiredPerSide = 1600;
        private const int SpawnBatchPerSide = 192;
        private const int FormationColumns = 25;
        private const int FormationSpacingCm = 50;
        private const int FrontOffsetCm = 260;
        private const int WaveIntervalTicks = 30;

        private static readonly QueryDescription StressMageQuery = new QueryDescription()
            .WithAll<Name, Team, MapEntity, AbilityStateBuffer>();

        private static readonly QueryDescription StressAnchorQuery = new QueryDescription()
            .WithAll<Name, Team, MapEntity, AttributeBuffer, WorldPositionCm>();

        private static readonly QueryDescription ProjectileQuery = new QueryDescription()
            .WithAll<ProjectileState>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly RuntimeEntitySpawnQueue _spawnQueue;
        private readonly OrderQueue _orders;
        private readonly InteractionShowcaseStressTelemetry _telemetry;
        private readonly MapId _stressMapId = new(InteractionShowcaseIds.StressMapId);
        private readonly int _castAbilityOrderTypeId;
        private readonly int _healthAttributeId;
        private readonly List<Entity> _redMages = new(DesiredPerSide);
        private readonly List<Entity> _blueMages = new(DesiredPerSide);

        private Entity _redAnchor;
        private Entity _blueAnchor;
        private int _requestedRed;
        private int _requestedBlue;
        private int _tickCounter;
        private int _redWaveCursor;
        private int _blueWaveCursor;

        public InteractionShowcaseStressSystem(
            GameEngine engine,
            RuntimeEntitySpawnQueue spawnQueue,
            OrderQueue orders,
            InteractionShowcaseStressTelemetry telemetry)
        {
            _engine = engine;
            _world = engine.World;
            _spawnQueue = spawnQueue;
            _orders = orders;
            _telemetry = telemetry;

            if (engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.GameConfig) is not Ludots.Core.Config.GameConfig config)
            {
                throw new InvalidOperationException("InteractionShowcaseStressSystem requires GameConfig to resolve castAbility order id.");
            }

            _castAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
            _healthAttributeId = AttributeRegistry.GetId("Health");
            _telemetry.Reset(DesiredPerSide);
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, InteractionShowcaseIds.StressMapId, StringComparison.OrdinalIgnoreCase))
            {
                ResetTracking();
                return;
            }

            _telemetry.IsActive = true;

            ResolveAnchors();
            if (!_world.IsAlive(_redAnchor) || !_world.IsAlive(_blueAnchor))
            {
                UpdateTelemetry(projectileCount: 0);
                return;
            }

            EnqueueSpawnRequests();
            CollectStressMages();

            _tickCounter++;
            if (_tickCounter >= WaveIntervalTicks && _redMages.Count > 0 && _blueMages.Count > 0)
            {
                DispatchWave();
                _tickCounter = 0;
            }

            UpdateTelemetry(CountProjectiles());
        }

        private void ResetTracking()
        {
            _redAnchor = default;
            _blueAnchor = default;
            _requestedRed = 0;
            _requestedBlue = 0;
            _tickCounter = 0;
            _redWaveCursor = 0;
            _blueWaveCursor = 0;
            _redMages.Clear();
            _blueMages.Clear();
            _telemetry.Reset(DesiredPerSide);
        }

        private void ResolveAnchors()
        {
            if (_world.IsAlive(_redAnchor) && _world.IsAlive(_blueAnchor))
            {
                return;
            }

            _redAnchor = default;
            _blueAnchor = default;

            _world.Query(in StressAnchorQuery, (Entity entity, ref Name name, ref Team _, ref MapEntity mapEntity, ref AttributeBuffer __, ref WorldPositionCm ___) =>
            {
                if (mapEntity.MapId != _stressMapId)
                {
                    return;
                }

                if (name.Value == InteractionShowcaseIds.StressRedAnchorName)
                {
                    _redAnchor = entity;
                }
                else if (name.Value == InteractionShowcaseIds.StressBlueAnchorName)
                {
                    _blueAnchor = entity;
                }
            });
        }

        private void EnqueueSpawnRequests()
        {
            if (!_world.TryGet(_redAnchor, out WorldPositionCm redAnchorPos) ||
                !_world.TryGet(_blueAnchor, out WorldPositionCm blueAnchorPos))
            {
                return;
            }

            int batchRed = Math.Min(SpawnBatchPerSide, DesiredPerSide - _requestedRed);
            int batchBlue = Math.Min(SpawnBatchPerSide, DesiredPerSide - _requestedBlue);

            for (int i = 0; i < batchRed; i++)
            {
                if (!TryEnqueueMage("interaction_stress_red_mage", redSide: true, _requestedRed + i, redAnchorPos.Value.ToWorldCmInt2()))
                {
                    break;
                }

                _requestedRed++;
            }

            for (int i = 0; i < batchBlue; i++)
            {
                if (!TryEnqueueMage("interaction_stress_blue_mage", redSide: false, _requestedBlue + i, blueAnchorPos.Value.ToWorldCmInt2()))
                {
                    break;
                }

                _requestedBlue++;
            }
        }

        private bool TryEnqueueMage(string templateId, bool redSide, int index, WorldCmInt2 anchorPos)
        {
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = templateId,
                WorldPositionCm = ComputeFormationPosition(redSide, index, anchorPos),
                MapId = _stressMapId,
            };

            return _spawnQueue.TryEnqueue(in request);
        }

        private static Fix64Vec2 ComputeFormationPosition(bool redSide, int index, in WorldCmInt2 anchorPos)
        {
            int column = index % FormationColumns;
            int row = index / FormationColumns;
            int rowCenterOffset = ((DesiredPerSide + FormationColumns - 1) / FormationColumns - 1) * FormationSpacingCm / 2;
            int x = redSide
                ? anchorPos.X + FrontOffsetCm + column * FormationSpacingCm
                : anchorPos.X - FrontOffsetCm - column * FormationSpacingCm;
            int y = anchorPos.Y - rowCenterOffset + row * FormationSpacingCm;
            return Fix64Vec2.FromInt(x, y);
        }

        private void CollectStressMages()
        {
            _redMages.Clear();
            _blueMages.Clear();

            _world.Query(in StressMageQuery, (Entity entity, ref Name name, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer _) =>
            {
                if (mapEntity.MapId != _stressMapId)
                {
                    return;
                }

                if (name.Value == "StressRedMage" && team.Id == 1)
                {
                    _redMages.Add(entity);
                }
                else if (name.Value == "StressBlueMage" && team.Id == 2)
                {
                    _blueMages.Add(entity);
                }
            });
        }

        private void DispatchWave()
        {
            int available = _orders.Capacity - _orders.Count;
            if (available <= 0)
            {
                return;
            }

            int redBudget = Math.Min(_redMages.Count, available / 2);
            int blueBudget = Math.Min(_blueMages.Count, available - redBudget);
            int issued = 0;

            issued += DispatchOrders(_redMages, ref _redWaveCursor, _blueAnchor, playerId: 1, redBudget);
            issued += DispatchOrders(_blueMages, ref _blueWaveCursor, _redAnchor, playerId: 2, blueBudget);

            if (issued > 0)
            {
                _telemetry.OrdersIssued += issued;
                _telemetry.WavesDispatched++;
            }
        }

        private int DispatchOrders(List<Entity> actors, ref int cursor, Entity target, int playerId, int budget)
        {
            if (budget <= 0 || !_world.IsAlive(target))
            {
                return 0;
            }

            int issued = 0;
            for (int i = 0; i < budget; i++)
            {
                if (actors.Count == 0)
                {
                    break;
                }

                if (cursor >= actors.Count)
                {
                    cursor = 0;
                }

                Entity actor = actors[cursor++];
                if (!_world.IsAlive(actor))
                {
                    continue;
                }

                var order = new Order
                {
                    OrderTypeId = _castAbilityOrderTypeId,
                    PlayerId = playerId,
                    Actor = actor,
                    Target = target,
                    SubmitMode = OrderSubmitMode.Immediate,
                    Args = new OrderArgs
                    {
                        I0 = 0,
                    }
                };

                if (_orders.TryEnqueue(in order))
                {
                    issued++;
                }
            }

            return issued;
        }

        private void UpdateTelemetry(int projectileCount)
        {
            _telemetry.RequestedRed = _requestedRed;
            _telemetry.RequestedBlue = _requestedBlue;
            _telemetry.LiveRed = _redMages.Count;
            _telemetry.LiveBlue = _blueMages.Count;
            _telemetry.ProjectileCount = projectileCount;
            _telemetry.QueueDepth = _orders.Count;
            _telemetry.RedAnchorHealth = ReadHealth(_redAnchor);
            _telemetry.BlueAnchorHealth = ReadHealth(_blueAnchor);
            if (projectileCount > _telemetry.PeakProjectileCount)
            {
                _telemetry.PeakProjectileCount = projectileCount;
            }
        }

        private int CountProjectiles()
        {
            int count = 0;
            _world.Query(in ProjectileQuery, (Entity _, ref ProjectileState __) => count++);
            return count;
        }

        private float ReadHealth(Entity entity)
        {
            if (_healthAttributeId < 0 ||
                !_world.IsAlive(entity) ||
                !_world.TryGet(entity, out AttributeBuffer attributes))
            {
                return 0f;
            }

            return attributes.GetCurrent(_healthAttributeId);
        }
    }
}
