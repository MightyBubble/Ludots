using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using RtsDemoMod.Components;

namespace RtsDemoMod.Systems
{
    public sealed class RtsScenarioSystem : ISystem<float>
    {
        private const int MoveAbilitySlotIndex = 0;

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly EntityBuilder _builder;
        private readonly QueryDescription _scenarioQuery = new QueryDescription().WithAll<RtsScenarioEntity>();
        private readonly QueryDescription _scenarioFocusQuery = new QueryDescription().WithAll<RtsScenarioEntity, WorldPositionCm, Team>();
        private readonly List<Entity> _playerUnits = new(128);
        private readonly List<Entity> _scenarioEntities = new(256);
        private bool _initialized;

        public RtsScenarioSystem(GameEngine engine, OrderQueue orders)
        {
            _engine = engine;
            _world = engine.World;
            _globals = engine.GlobalContext;
            _orders = orders;

            var templates = new Dictionary<string, EntityTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in engine.MapLoader.TemplateRegistry.GetAll())
            {
                templates[template.Id] = template;
            }

            _builder = new EntityBuilder(_world, templates);
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!IsActive())
            {
                _initialized = false;
                return;
            }

            var input = ResolveInput();
            if (!_initialized || !HasScenarioEntities())
            {
                LoadScenario(RtsScenarioId.PassThrough);
                _initialized = true;
            }

            if (input == null)
            {
                return;
            }

            if (input.PressedThisFrame("Hotkey1"))
            {
                LoadScenario(RtsScenarioId.PassThrough);
            }
            else if (input.PressedThisFrame("Hotkey2"))
            {
                LoadScenario(RtsScenarioId.Bottleneck);
            }
            else if (input.PressedThisFrame("Hotkey3"))
            {
                LoadScenario(RtsScenarioId.Formation);
            }
            else if (input.PressedThisFrame("Hotkey4"))
            {
                var state = ResolveSelectionState();
                state.ShowVelocityVectors = !state.ShowVelocityVectors;
            }
        }

        private void LoadScenario(RtsScenarioId scenarioId)
        {
            ClearScenarioEntities();
            var controller = RtsUnitRuntimeSetup.EnsureController(_world, _globals);
            var state = ResolveSelectionState();
            state.CurrentScenario = scenarioId;
            state.HasLastCommand = false;

            switch (scenarioId)
            {
                case RtsScenarioId.PassThrough:
                    SpawnPassThrough();
                    break;
                case RtsScenarioId.Bottleneck:
                    SpawnBottleneck();
                    break;
                default:
                    SpawnFormation();
                    break;
            }

            FocusCameraOnScenarioAgents();
            SelectionRuntime.ApplySelection(_world, _globals, controller, _playerUnits, SelectionApplyMode.Replace);
        }

        private void SpawnPassThrough()
        {
            _playerUnits.Clear();
            var marines = SpawnGrid("rts_marine", 6, 4, -2800, -450, 120, 120, collectPlayer: true);
            var zerglings = SpawnGrid("rts_zergling", 6, 4, 2800, -420, 110, 110, collectPlayer: false);
            IssueMoveFormation(marines, new WorldCmInt2(2800, 0));
            IssueMoveFormation(zerglings, new WorldCmInt2(-2800, 0));
        }

        private void SpawnBottleneck()
        {
            _playerUnits.Clear();
            var marines = SpawnGrid("rts_marine", 5, 5, -300, -2600, 120, 120, collectPlayer: true);
            SpawnBlockerColumn(-420, -220, 220);
            SpawnBlockerColumn(420, -220, 220);
            IssueMoveFormation(marines, new WorldCmInt2(0, 2600));
        }

        private void SpawnFormation()
        {
            _playerUnits.Clear();
            var marines = SpawnGrid("rts_marine", 8, 5, -1100, -1600, 120, 120, collectPlayer: true);
            IssueMoveFormation(marines, new WorldCmInt2(0, 1600));
        }

        private List<Entity> SpawnGrid(string templateId, int columns, int rows, int startX, int startY, int spacingX, int spacingY, bool collectPlayer)
        {
            var spawned = new List<Entity>(columns * rows);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int x = startX + col * spacingX;
                    int y = startY + row * spacingY;
                    var entity = SpawnTemplate(templateId, x, y);
                    spawned.Add(entity);
                    if (collectPlayer)
                    {
                        _playerUnits.Add(entity);
                    }
                }
            }

            return spawned;
        }

        private void SpawnBlockerColumn(int x, int gapMinY, int gapMaxY)
        {
            for (int y = -2200; y <= 2200; y += 140)
            {
                if (y >= gapMinY && y <= gapMaxY)
                {
                    continue;
                }

                SpawnTemplate("rts_blocker", x, y);
            }
        }

        private Entity SpawnTemplate(string templateId, int x, int y)
        {
            var entity = _builder.UseTemplate(templateId).WithOverride("WorldPositionCm", CreateWorldPositionNode(x, y)).Build();
            _world.Add(entity, new RtsScenarioEntity());
            RtsUnitRuntimeSetup.EnsureRuntimeComponents(_world, entity);
            return entity;
        }

        private void IssueMoveFormation(IReadOnlyList<Entity> actors, in WorldCmInt2 targetCm)
        {
            int castAbilityOrderTagId = ResolveCastAbilityOrderTagId();
            var assignments = RtsFormationPlanner.Plan(_world, actors, targetCm);
            for (int i = 0; i < assignments.Count; i++)
            {
                var assignment = assignments[i];
                if (!_world.IsAlive(assignment.Actor) || !_world.Has<OrderBuffer>(assignment.Actor))
                {
                    continue;
                }

                var order = new Order
                {
                    OrderTagId = castAbilityOrderTagId,
                    PlayerId = 0,
                    Actor = assignment.Actor,
                    SubmitMode = OrderSubmitMode.Immediate,
                    Args = new OrderArgs
                    {
                        I0 = MoveAbilitySlotIndex,
                        Spatial = new OrderSpatial
                        {
                            Kind = OrderSpatialKind.WorldCm,
                            Mode = OrderCollectionMode.Single,
                            WorldCm = new Vector3(assignment.TargetCm.X.ToFloat(), 0f, assignment.TargetCm.Y.ToFloat())
                        }
                    }
                };

                _orders.TryEnqueue(order);
            }
        }

        private int ResolveCastAbilityOrderTagId()
        {
            var config = _engine.GetService(CoreServiceKeys.GameConfig);
            return config.Constants.OrderTags["castAbility"];
        }

        private bool HasScenarioEntities()
        {
            foreach (ref var chunk in _world.Query(in _scenarioQuery))
            {
                if (chunk.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearScenarioEntities()
        {
            _scenarioEntities.Clear();
            foreach (ref var chunk in _world.Query(in _scenarioQuery))
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    _scenarioEntities.Add(chunk.Entity(i));
                }
            }

            for (int i = 0; i < _scenarioEntities.Count; i++)
            {
                var entity = _scenarioEntities[i];
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }
        }

        private void FocusCameraOnScenarioAgents()
        {
            float sumX = 0f;
            float sumY = 0f;
            int count = 0;
            foreach (ref var chunk in _world.Query(in _scenarioFocusQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                var teams = chunk.GetSpan<Team>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (teams[i].Id == 0)
                    {
                        continue;
                    }

                    sumX += positions[i].Value.X.ToFloat();
                    sumY += positions[i].Value.Y.ToFloat();
                    count++;
                }
            }

            if (count <= 0)
            {
                return;
            }

            _engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                TargetCm = new Vector2(sumX / count, sumY / count),
            });
        }

        private static JsonObject CreateWorldPositionNode(int x, int y)
        {
            return new JsonObject
            {
                ["Value"] = new JsonObject
                {
                    ["X"] = x,
                    ["Y"] = y,
                }
            };
        }

        private PlayerInputHandler? ResolveInput()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var obj) && obj is PlayerInputHandler input)
            {
                return input;
            }

            return null;
        }

        private RtsSelectionState ResolveSelectionState()
        {
            if (_globals.TryGetValue(RtsDemoKeys.SelectionState, out var obj) && obj is RtsSelectionState state)
            {
                return state;
            }

            state = new RtsSelectionState();
            _globals[RtsDemoKeys.SelectionState] = state;
            return state;
        }

        private bool IsActive()
        {
            return _globals.TryGetValue(RtsDemoKeys.IsActiveMap, out var obj) && obj is bool enabled && enabled;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}





