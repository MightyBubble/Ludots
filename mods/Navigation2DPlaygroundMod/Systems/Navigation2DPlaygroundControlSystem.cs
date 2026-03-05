using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod.Systems
{
    public sealed class Navigation2DPlaygroundControlSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;

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

            bool shouldRespawn = false;

            if (_engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DPlayground_ResetScenario, out var resetObj) &&
                resetObj is bool reset && reset)
            {
                _engine.GlobalContext.Remove(ContextKeys.Navigation2DPlayground_ResetScenario);
                shouldRespawn = true;
            }

            if (_engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DPlayground_AgentDeltaPerTeam, out var deltaObj) &&
                deltaObj is int delta && delta != 0)
            {
                _engine.GlobalContext.Remove(ContextKeys.Navigation2DPlayground_AgentDeltaPerTeam);

                int next = Navigation2DPlaygroundState.AgentsPerTeam + delta;
                if (next < 0) next = 0;
                if (next > 25000) next = 25000;
                Navigation2DPlaygroundState.AgentsPerTeam = next;
                shouldRespawn = true;
            }

            if (!shouldRespawn) return;

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

            _engine.GlobalContext[ContextKeys.Navigation2DPlayground_AgentsPerTeam] = Navigation2DPlaygroundState.AgentsPerTeam;
            _engine.GlobalContext[ContextKeys.Navigation2DPlayground_LiveAgentsTotal] = total;
        }
    }
}
