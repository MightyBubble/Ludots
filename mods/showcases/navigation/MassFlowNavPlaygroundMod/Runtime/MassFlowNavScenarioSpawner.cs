using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Components;

namespace MassFlowNavPlaygroundMod.Runtime
{
    internal static class MassFlowNavScenarioSpawner
    {
        private static readonly (float X, float Y, float Radius)[] ExternalObstacleLayout =
        {
            (30f, 50f, 5f),
            (50f, 30f, 4f),
            (50f, 70f, 4f),
            (70f, 50f, 5f),
            (50f, 50f, 3f)
        };

        private static readonly QueryDescription SceneQuery = new QueryDescription()
            .WithAll<MassFlowNavPlaygroundEntityTag>();

        public static void Respawn(GameEngine engine, MassFlowNavPlaygroundState state)
        {
            if (engine.CurrentMapSession == null)
            {
                return;
            }

            World world = engine.World;
            world.Destroy(in SceneQuery);
            state.ResetSceneState();

            MapId mapId = engine.CurrentMapSession.MapId;

            Entity sceneRoot = world.Create(
                default(MassFlowNavPlaygroundEntityTag),
                default(MassFlowNavSceneRootTag),
                new MapEntity { MapId = mapId });
            state.SceneRootEntity = sceneRoot;

            Entity controller = world.Create(
                default(MassFlowNavPlaygroundEntityTag),
                default(MassFlowNavControllerTag),
                new MapEntity { MapId = mapId },
                new PlayerOwner { PlayerId = MassFlowNavPlaygroundIds.LocalPlayerId },
                new Name { Value = MassFlowNavPlaygroundIds.ControllerName });
            state.ControllerEntity = controller;
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, controller);

            state.Team0FlowGoalEntity = world.Create(
                default(MassFlowNavPlaygroundEntityTag),
                new MapEntity { MapId = mapId },
                new NavFlowGoal2D
                {
                    FlowId = 0,
                    GoalCm = Fix64Vec2.FromInt(9000, 5000),
                    RadiusCm = Fix64.FromInt(150)
                });

            state.Team1FlowGoalEntity = world.Create(
                default(MassFlowNavPlaygroundEntityTag),
                new MapEntity { MapId = mapId },
                new NavFlowGoal2D
                {
                    FlowId = 1,
                    GoalCm = Fix64Vec2.FromInt(1000, 5000),
                    RadiusCm = Fix64.FromInt(150)
                });

            for (int i = 0; i < ExternalObstacleLayout.Length; i++)
            {
                SpawnObstacle(world, mapId, ExternalObstacleLayout[i]);
            }

            int totalUnits = Math.Max(1000, state.DesiredUnitCount);
            int friendlyCount = totalUnits / 2;
            int enemyCount = totalUnits - friendlyCount;
            state.SetPopulationCounts(friendlyCount, enemyCount);

            SpawnAgents(world, mapId, friendlyCount, MassFlowNavPlaygroundIds.FriendlyTeamId, 0, 500, 2000, 1000, 9000, selectable: true);
            SpawnAgents(world, mapId, enemyCount, MassFlowNavPlaygroundIds.EnemyTeamId, 1, 8000, 9500, 1000, 9000, selectable: false);
        }

        private static void SpawnAgents(
            World world,
            MapId mapId,
            int count,
            int teamId,
            int flowId,
            int minXcm,
            int maxXcm,
            int minYcm,
            int maxYcm,
            bool selectable)
        {
            var rng = new Random(42 + flowId);
            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(minXcm, maxXcm + 1);
                int y = rng.Next(minYcm, maxYcm + 1);
                Fix64Vec2 position = Fix64Vec2.FromInt(x, y);

                Entity entity = world.Create(
                    default(MassFlowNavPlaygroundEntityTag),
                    new MapEntity { MapId = mapId },
                    new Team { Id = teamId },
                    new NavAgent2D(),
                    new NavFlowBinding2D { SurfaceId = 0, FlowId = flowId },
                    new MassFlowNavTeamFlowAssignment { SurfaceId = 0, FlowId = flowId, TeamId = teamId },
                    new NavKinematics2D
                    {
                        MaxSpeedCmPerSec = Fix64.FromInt(800),
                        MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                        RadiusCm = Fix64.FromInt(40),
                        NeighborDistCm = Fix64.FromInt(400),
                        TimeHorizonSec = Fix64.FromInt(2),
                        MaxNeighbors = 16
                    },
                    new Position2D { Value = position },
                    new PreviousPosition2D { Value = position },
                    Velocity2D.Zero,
                    Mass2D.FromFloat(1f, 1f),
                    new WorldPositionCm { Value = position },
                    new PreviousWorldPositionCm { Value = position },
                    new VisualTransform
                    {
                        Position = WorldUnits.WorldCmToVisualMeters(position, yMeters: 0f),
                        Rotation = System.Numerics.Quaternion.Identity,
                        Scale = System.Numerics.Vector3.One
                    },
                    new CullState { IsVisible = true, LOD = LODLevel.High });

                if (selectable)
                {
                    world.Add(entity, new SelectionSelectableTag());
                    world.Add(entity, SelectionSelectableState.EnabledByDefault);
                }
            }
        }

        private static void SpawnObstacle(
            World world,
            MapId mapId,
            (float X, float Y, float Radius) obstacle)
        {
            int x = (int)MathF.Round(obstacle.X * 100f);
            int y = (int)MathF.Round(obstacle.Y * 100f);
            int radiusCm = (int)MathF.Round(obstacle.Radius * 100f);
            int shapeDataIndex = Ludots.Core.Physics2D.ShapeDataStorage2D.RegisterCircle(Fix64.FromInt(radiusCm));
            Fix64Vec2 position = Fix64Vec2.FromInt(x, y);

            world.Create(
                default(MassFlowNavPlaygroundEntityTag),
                default(MassFlowNavObstacleTag),
                new MapEntity { MapId = mapId },
                new NavAgent2D(),
                new NavObstacle2D
                {
                    Shape = NavObstacleShape2D.Circle,
                    ShapeDataIndex = shapeDataIndex
                },
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.Zero,
                    MaxAccelCmPerSec2 = Fix64.Zero,
                    RadiusCm = Fix64.FromInt(radiusCm),
                    NeighborDistCm = Fix64.Zero,
                    TimeHorizonSec = Fix64.OneValue,
                    MaxNeighbors = 0
                },
                new Position2D { Value = position },
                new PreviousPosition2D { Value = position },
                Velocity2D.Zero,
                Mass2D.Static,
                new WorldPositionCm { Value = position },
                new PreviousWorldPositionCm { Value = position },
                new VisualTransform
                {
                    Position = WorldUnits.WorldCmToVisualMeters(position, yMeters: 0f),
                    Rotation = System.Numerics.Quaternion.Identity,
                    Scale = System.Numerics.Vector3.One
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });
        }
    }
}
