using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Navigation2DPlaygroundMod.Input;

namespace Navigation2DPlaygroundMod.Systems
{
    public sealed class Navigation2DPlaygroundControlSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private PlayerInputHandler? _input;

        private static readonly QueryDescription _scenarioQuery = new QueryDescription()
            .WithAll<NavPlaygroundTeam>();

        private static readonly QueryDescription _flowGoalQuery = new QueryDescription()
            .WithAll<NavFlowGoal2D>();

        public Navigation2DPlaygroundControlSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float deltaTime)
        {
            if (!Navigation2DPlaygroundState.Enabled) return;
            ResolveInput();
            if (_input == null) return;

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var runtimeObj) &&
                runtimeObj is Navigation2DRuntime navRuntime)
            {
                HandlePressed(Navigation2DPlaygroundInputActions.ToggleFlowEnabled, () =>
                {
                    navRuntime.FlowEnabled = !navRuntime.FlowEnabled;
                });
                HandlePressed(Navigation2DPlaygroundInputActions.ToggleFlowDebug, () =>
                {
                    navRuntime.FlowDebugEnabled = !navRuntime.FlowDebugEnabled;
                });
                HandlePressed(Navigation2DPlaygroundInputActions.CycleFlowDebugMode, () =>
                {
                    navRuntime.FlowDebugMode = (navRuntime.FlowDebugMode + 1) % 3;
                });
                HandlePressed(Navigation2DPlaygroundInputActions.IncreaseFlowIterations, () =>
                {
                    navRuntime.FlowIterationsPerTick = Math.Clamp(navRuntime.FlowIterationsPerTick + 512, 0, 131072);
                });
                HandlePressed(Navigation2DPlaygroundInputActions.DecreaseFlowIterations, () =>
                {
                    navRuntime.FlowIterationsPerTick = Math.Clamp(navRuntime.FlowIterationsPerTick - 512, 0, 131072);
                });
            }

            HandlePressed(Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam, () =>
            {
                AdjustAgentsPerTeam(500);
            });
            HandlePressed(Navigation2DPlaygroundInputActions.DecreaseAgentsPerTeam, () =>
            {
                AdjustAgentsPerTeam(-500);
            });
            HandlePressed(Navigation2DPlaygroundInputActions.ResetScenario, RespawnScenario);
        }

        private void ResolveInput()
        {
            if (_input != null) return;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler handler)
            {
                _input = handler;
            }
        }

        private void HandlePressed(string actionId, Action onPressed)
        {
            if (_input!.PressedThisFrame(actionId))
            {
                onPressed();
            }
        }

        private void AdjustAgentsPerTeam(int delta)
        {
            int next = Navigation2DPlaygroundState.AgentsPerTeam + delta;
            if (next < 0) next = 0;
            if (next > 25000) next = 25000;
            if (next == Navigation2DPlaygroundState.AgentsPerTeam) return;
            Navigation2DPlaygroundState.AgentsPerTeam = next;
            RespawnScenario();
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        private void RespawnScenario()
        {
            _world.Destroy(in _scenarioQuery);
            _world.Destroy(in _flowGoalQuery);

            SpawnScenario(_world, Navigation2DPlaygroundState.AgentsPerTeam);
            PublishCounts();
        }

        public static void SpawnScenario(World world, int agentsPerTeam)
        {
            int xLeft = -9000;
            int xRight = 9000;

            world.Create(new NavFlowGoal2D
            {
                FlowId = 0,
                GoalCm = Fix64Vec2.FromInt(xRight, 0),
                RadiusCm = Fix64.FromInt(0)
            });

            world.Create(new NavFlowGoal2D
            {
                FlowId = 1,
                GoalCm = Fix64Vec2.FromInt(xLeft, 0),
                RadiusCm = Fix64.FromInt(0)
            });

            var kin = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(800),
                MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                RadiusCm = Fix64.FromInt(40),
                NeighborDistCm = Fix64.FromInt(400),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 16
            };

            int spacing = 120;
            int cols = agentsPerTeam <= 0 ? 0 : (int)Math.Ceiling(Math.Sqrt(agentsPerTeam));
            int rows = cols <= 0 ? 0 : (int)Math.Ceiling(agentsPerTeam / (double)cols);
            int yStart = -(rows - 1) * spacing / 2;

            int spawned = 0;
            for (int r = 0; r < rows && spawned < agentsPerTeam; r++)
            {
                int y = yStart + r * spacing;
                for (int c = 0; c < cols && spawned < agentsPerTeam; c++)
                {
                    int x0 = xLeft - c * spacing;
                    int x1 = xRight + c * spacing;

                    world.Create(
                        new NavAgent2D(),
                        new NavFlowBinding2D { SurfaceId = 0, FlowId = 0 },
                        new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(xRight, y), RadiusCm = Fix64.Zero },
                        kin,
                        new Position2D { Value = Fix64Vec2.FromInt(x0, y) },
                        Velocity2D.Zero,
                        Mass2D.FromFloat(1f, 1f),
                        new WorldPositionCm { Value = Fix64Vec2.FromInt(x0, y) },
                        new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(x0, y) },
                        VisualTransform.Default,
                        new NavPlaygroundTeam { Id = 0 }
                    );

                    world.Create(
                        new NavAgent2D(),
                        new NavFlowBinding2D { SurfaceId = 0, FlowId = 1 },
                        new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(xLeft, y), RadiusCm = Fix64.Zero },
                        kin,
                        new Position2D { Value = Fix64Vec2.FromInt(x1, y) },
                        Velocity2D.Zero,
                        Mass2D.FromFloat(1f, 1f),
                        new WorldPositionCm { Value = Fix64Vec2.FromInt(x1, y) },
                        new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(x1, y) },
                        VisualTransform.Default,
                        new NavPlaygroundTeam { Id = 1 }
                    );

                    spawned++;
                }
            }
        }

        private void PublishCounts()
        {
            int total = 0;
            foreach (ref var chunk in _world.Query(in _scenarioQuery))
            {
                total += chunk.Count;
            }

            _engine.SetService(Navigation2DPlaygroundKeys.AgentsPerTeam, Navigation2DPlaygroundState.AgentsPerTeam);
            _engine.SetService(Navigation2DPlaygroundKeys.LiveAgentsTotal, total);
        }
    }
}
