using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod.Systems
{
    public readonly record struct Navigation2DPlaygroundSpawnSummary(
        string ScenarioId,
        string ScenarioName,
        int TeamCount,
        int DynamicAgents,
        int BlockerCount);

    public static class Navigation2DPlaygroundScenarioSpawner
    {
        private const string AgentVisualTemplateKey = "formation_playground.agent";
        private const string BlockerVisualTemplateKey = "formation_playground.blocker";
        private const int FormationBodyHalfWidthCm = 60;
        private const int FormationBodyHalfHeightCm = 40;

        private static int _formationBodyShapeIndex = -1;
        private static readonly Dictionary<int, int> BlockerShapeIndicesByHalfExtentCm = new();

        public static Navigation2DPlaygroundConfig GetPlaygroundConfig(GameConfig? gameConfig)
        {
            return (gameConfig?.Navigation2D ?? new Navigation2DConfig()).CloneValidated().Playground;
        }

        public static Navigation2DPlaygroundScenarioConfig GetScenario(Navigation2DPlaygroundConfig playgroundConfig, int scenarioIndex)
        {
            if (playgroundConfig.Scenarios.Count == 0)
            {
                throw new InvalidOperationException("Navigation2D playground scenario catalog is empty.");
            }

            return playgroundConfig.Scenarios[ClampScenarioIndex(playgroundConfig, scenarioIndex)];
        }

        public static int ClampScenarioIndex(Navigation2DPlaygroundConfig playgroundConfig, int scenarioIndex)
        {
            if (playgroundConfig.Scenarios.Count == 0)
            {
                return 0;
            }

            if (scenarioIndex < 0)
            {
                return playgroundConfig.Scenarios.Count - 1;
            }

            if (scenarioIndex >= playgroundConfig.Scenarios.Count)
            {
                return 0;
            }

            return scenarioIndex;
        }

        public static Navigation2DPlaygroundSpawnSummary SpawnScenario(World world, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam)
        {
            return SpawnScenario(world, globals: null, scenario, agentsPerTeam);
        }

        public static Navigation2DPlaygroundSpawnSummary SpawnScenario(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));

            int dynamicAgents;
            int blockerCount = 0;
            switch (scenario.Kind)
            {
                case Navigation2DPlaygroundScenarioKind.PassThrough:
                    dynamicAgents = SpawnPassThrough(world, globals, scenario, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.OrthogonalCross:
                    dynamicAgents = SpawnOrthogonalCross(world, globals, scenario, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.Bottleneck:
                    dynamicAgents = SpawnBottleneck(world, globals, scenario, agentsPerTeam, out blockerCount);
                    break;
                case Navigation2DPlaygroundScenarioKind.LaneMerge:
                    dynamicAgents = SpawnLaneMerge(world, globals, scenario, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.CircleSwap:
                    dynamicAgents = SpawnCircleSwap(world, globals, scenario, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.GoalQueue:
                    dynamicAgents = SpawnGoalQueue(world, globals, scenario, agentsPerTeam, out blockerCount);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Navigation2D playground scenario kind: {scenario.Kind}");
            }

            return new Navigation2DPlaygroundSpawnSummary(
                scenario.Id,
                scenario.Name,
                scenario.TeamCount,
                dynamicAgents,
                blockerCount);
        }

        public static int SpawnDynamicBatch(
            World world,
            Dictionary<string, object>? globals,
            int teamId,
            Vector2 centerCm,
            int count,
            int spacingCm,
            int goalRadiusCm)
        {
            GetGridLayout(count, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < count; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                Vector2 offset = new(
                    GetCenteredOffset(col, cols, spacingCm),
                    GetCenteredOffset(row, rows, spacingCm));
                Vector2 position = centerCm + offset;
                SpawnDynamicAgent(world, globals, teamId, position, position, goalRadiusCm, flowId: null);
                spawned++;
            }

            return spawned;
        }

        public static int SpawnBlockerBatch(World world, Dictionary<string, object>? globals, Vector2 centerCm, int count, int spacingCm, int radiusCm)
        {
            GetGridLayout(count, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < count; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int x = (int)MathF.Round(centerCm.X) + GetCenteredOffset(col, cols, spacingCm);
                int y = (int)MathF.Round(centerCm.Y) + GetCenteredOffset(row, rows, spacingCm);
                SpawnBlocker(world, globals, x, y, radiusCm);
                spawned++;
            }

            return spawned;
        }

        public static void ApplyMoveFormation(
            World world,
            ReadOnlySpan<Entity> agents,
            Vector2 targetCm,
            int spacingCm,
            int goalRadiusCm)
        {
            GetGridLayout(agents.Length, out int cols, out int rows);
            int assigned = 0;
            for (int i = 0; i < agents.Length; i++)
            {
                Entity entity = agents[i];
                if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<NavGoal2D>(entity) || world.Has<NavPlaygroundBlocker>(entity))
                {
                    continue;
                }

                GetGridCell(assigned, cols, out int row, out int col);
                Vector2 offset = new(
                    GetCenteredOffset(col, cols, spacingCm),
                    GetCenteredOffset(row, rows, spacingCm));

                ref var goal = ref world.Get<NavGoal2D>(entity);
                goal.Kind = NavGoalKind2D.Point;
                goal.TargetCm = Fix64Vec2.FromInt(
                    (int)MathF.Round(targetCm.X + offset.X),
                    (int)MathF.Round(targetCm.Y + offset.Y));
                goal.RadiusCm = Fix64.FromInt(goalRadiusCm);
                assigned++;
            }
        }

        private static int SpawnPassThrough(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam)
        {
            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int laneY = GetCenteredOffset(row, rows, scenario.FormationSpacingCm);
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;

                SpawnDynamicAgent(world, globals, 0, new Vector2(-depth, laneY), new Vector2(scenario.GoalOffsetCm, laneY), scenario.GoalRadiusCm, flowId: null);
                SpawnDynamicAgent(world, globals, 1, new Vector2(depth, laneY), new Vector2(-scenario.GoalOffsetCm, laneY), scenario.GoalRadiusCm, flowId: null);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnOrthogonalCross(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam)
        {
            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int lane = GetCenteredOffset(row, rows, scenario.FormationSpacingCm);
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;

                SpawnDynamicAgent(world, globals, 0, new Vector2(-depth, lane), new Vector2(scenario.GoalOffsetCm, lane), scenario.GoalRadiusCm, flowId: null);
                SpawnDynamicAgent(world, globals, 1, new Vector2(lane, -depth), new Vector2(lane, scenario.GoalOffsetCm), scenario.GoalRadiusCm, flowId: null);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnBottleneck(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam, out int blockerCount)
        {
            int spawned = SpawnPassThrough(world, globals, scenario, agentsPerTeam);
            blockerCount = SpawnVerticalGate(world, globals, scenario.CorridorHalfWidthCm, scenario.BlockerRadiusCm, scenario.BlockerCount, scenario.BlockerSpacingCm);
            return spawned;
        }

        private static int SpawnLaneMerge(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam)
        {
            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int lane = GetCenteredOffset(row, rows, scenario.FormationSpacingCm);
                int mergedGoalY = lane / 4;
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;

                SpawnDynamicAgent(world, globals, 0, new Vector2(-depth, scenario.LaneOffsetCm + lane), new Vector2(scenario.GoalOffsetCm, mergedGoalY), scenario.GoalRadiusCm, flowId: null);
                SpawnDynamicAgent(world, globals, 1, new Vector2(-depth, -scenario.LaneOffsetCm + lane), new Vector2(scenario.GoalOffsetCm, mergedGoalY), scenario.GoalRadiusCm, flowId: null);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnCircleSwap(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam)
        {
            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                float rowT = rows <= 1 ? 0.5f : row / (float)(rows - 1);
                float leftAngle = MathF.PI * (0.5f + rowT);
                float rightAngle = MathF.PI * (-0.5f + rowT);
                float radius = scenario.RingRadiusCm + col * scenario.FormationSpacingCm;

                Vector2 leftPos = FromPolar(radius, leftAngle);
                Vector2 rightPos = FromPolar(radius, rightAngle);
                SpawnDynamicAgent(world, globals, 0, leftPos, -leftPos, scenario.GoalRadiusCm, flowId: null);
                SpawnDynamicAgent(world, globals, 1, rightPos, -rightPos, scenario.GoalRadiusCm, flowId: null);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnGoalQueue(World world, Dictionary<string, object>? globals, Navigation2DPlaygroundScenarioConfig scenario, int agentsPerTeam, out int blockerCount)
        {
            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int lane = GetCenteredOffset(row, rows, scenario.FormationSpacingCm) / 2;
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;
                SpawnDynamicAgent(world, globals, 0, new Vector2(-depth, lane), new Vector2(scenario.GoalOffsetCm, 0), scenario.GoalRadiusCm, flowId: null);
                spawned++;
            }

            blockerCount = SpawnHorizontalCorridor(world, globals, scenario.GoalOffsetCm, scenario.CorridorHalfWidthCm, scenario.BlockerRadiusCm, scenario.BlockerCount, scenario.BlockerSpacingCm);
            return spawned;
        }

        private static void SpawnDynamicAgent(World world, Dictionary<string, object>? globals, int teamId, Vector2 start, Vector2 goal, int goalRadiusCm, int? flowId)
        {
            int shapeIndex = GetFormationBodyShapeIndex();
            var kinematics = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(800),
                MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                RadiusCm = Fix64.FromInt(60),
                NeighborDistCm = Fix64.FromInt(400),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 16,
            };

            var position = Fix64Vec2.FromVector2(start);
            var goalPosition = Fix64Vec2.FromVector2(goal);
            var rotation = CreateFormationBodyRotation(start, goal);
            var facing = new FacingDirection { AngleRad = rotation.Value.ToFloat() };
            bool controllable = teamId == 0;
            if (flowId.HasValue)
            {
                if (controllable)
                {
                    Entity entity = world.Create(
                        new NavAgent2D(),
                        new NavFlowBinding2D { SurfaceId = 0, FlowId = flowId.Value },
                        new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                        kinematics,
                        new Position2D { Value = position },
                        rotation,
                        Velocity2D.Zero,
                        Mass2D.FromFloat(1f, 1f),
                        new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex },
                        new WorldPositionCm { Value = position },
                        new PreviousWorldPositionCm { Value = position },
                        VisualTransform.Default,
                        new CullState { IsVisible = true, LOD = LODLevel.High },
                        default(SelectionSelectableTag),
                        SelectionSelectableState.EnabledByDefault,
                        facing,
                        new NavPlaygroundTeam { Id = (byte)teamId },
                        new NavPlaygroundControllable());
                    AttachPresentation(world, entity, globals, AgentVisualTemplateKey, teamId);
                }
                else
                {
                    Entity entity = world.Create(
                        new NavAgent2D(),
                        new NavFlowBinding2D { SurfaceId = 0, FlowId = flowId.Value },
                        new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                        kinematics,
                        new Position2D { Value = position },
                        rotation,
                        Velocity2D.Zero,
                        Mass2D.FromFloat(1f, 1f),
                        new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex },
                        new WorldPositionCm { Value = position },
                        new PreviousWorldPositionCm { Value = position },
                        VisualTransform.Default,
                        new CullState { IsVisible = true, LOD = LODLevel.High },
                        facing,
                        new NavPlaygroundTeam { Id = (byte)teamId });
                    AttachPresentation(world, entity, globals, AgentVisualTemplateKey, teamId);
                }
                return;
            }

            if (controllable)
            {
                Entity entity = world.Create(
                    new NavAgent2D(),
                    new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                    kinematics,
                    new Position2D { Value = position },
                    rotation,
                    Velocity2D.Zero,
                    Mass2D.FromFloat(1f, 1f),
                    new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex },
                    new WorldPositionCm { Value = position },
                    new PreviousWorldPositionCm { Value = position },
                    VisualTransform.Default,
                    new CullState { IsVisible = true, LOD = LODLevel.High },
                    default(SelectionSelectableTag),
                    SelectionSelectableState.EnabledByDefault,
                    facing,
                    new NavPlaygroundTeam { Id = (byte)teamId },
                    new NavPlaygroundControllable());
                AttachPresentation(world, entity, globals, AgentVisualTemplateKey, teamId);
            }
            else
            {
                Entity entity = world.Create(
                    new NavAgent2D(),
                    new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                    kinematics,
                    new Position2D { Value = position },
                    rotation,
                    Velocity2D.Zero,
                    Mass2D.FromFloat(1f, 1f),
                    new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex },
                    new WorldPositionCm { Value = position },
                    new PreviousWorldPositionCm { Value = position },
                    VisualTransform.Default,
                    new CullState { IsVisible = true, LOD = LODLevel.High },
                    facing,
                    new NavPlaygroundTeam { Id = (byte)teamId });
                AttachPresentation(world, entity, globals, AgentVisualTemplateKey, teamId);
            }
        }

        private static int SpawnVerticalGate(World world, Dictionary<string, object>? globals, int corridorHalfWidthCm, int blockerRadiusCm, int blockerCount, int blockerSpacingCm)
        {
            int spawned = 0;
            int ring = 0;
            while (spawned < blockerCount)
            {
                int y = corridorHalfWidthCm + blockerRadiusCm + ring * blockerSpacingCm;
                SpawnBlocker(world, globals, 0, y, blockerRadiusCm);
                spawned++;
                if (spawned < blockerCount)
                {
                    SpawnBlocker(world, globals, 0, -y, blockerRadiusCm);
                    spawned++;
                }

                ring++;
            }

            return spawned;
        }

        private static int SpawnHorizontalCorridor(World world, Dictionary<string, object>? globals, int goalOffsetCm, int corridorHalfWidthCm, int blockerRadiusCm, int blockerCount, int blockerSpacingCm)
        {
            int spawned = 0;
            int column = 0;
            int wallY = corridorHalfWidthCm + blockerRadiusCm;
            while (spawned < blockerCount)
            {
                int x = goalOffsetCm - blockerRadiusCm - column * blockerSpacingCm;
                SpawnBlocker(world, globals, x, wallY, blockerRadiusCm);
                spawned++;
                if (spawned < blockerCount)
                {
                    SpawnBlocker(world, globals, x, -wallY, blockerRadiusCm);
                    spawned++;
                }

                column++;
            }

            return spawned;
        }

        private static void SpawnBlocker(World world, Dictionary<string, object>? globals, int x, int y, int radiusCm)
        {
            var position = Fix64Vec2.FromInt(x, y);
            int shapeIndex = GetBlockerShapeIndex(radiusCm);
            Entity entity = world.Create(
                new NavAgent2D(),
                new NavObstacle2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.Zero,
                    MaxAccelCmPerSec2 = Fix64.Zero,
                    RadiusCm = Fix64.FromInt(radiusCm),
                    NeighborDistCm = Fix64.Zero,
                    TimeHorizonSec = Fix64.OneValue,
                    MaxNeighbors = 0,
                },
                new Position2D { Value = position },
                Rotation2D.Identity,
                Velocity2D.Zero,
                Mass2D.Static,
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex },
                new WorldPositionCm { Value = position },
                new PreviousWorldPositionCm { Value = position },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new FacingDirection { AngleRad = 0f },
                new NavPlaygroundTeam { Id = byte.MaxValue },
                new NavPlaygroundBlocker());
            AttachPresentation(world, entity, globals, BlockerVisualTemplateKey, teamId: null);
        }

        private static Vector2 FromPolar(float radius, float angleRad)
        {
            return new Vector2(MathF.Cos(angleRad) * radius, MathF.Sin(angleRad) * radius);
        }

        private static int GetFormationBodyShapeIndex()
        {
            if (_formationBodyShapeIndex >= 0)
            {
                return _formationBodyShapeIndex;
            }

            _formationBodyShapeIndex = ShapeDataStorage2D.RegisterBox(FormationBodyHalfWidthCm, FormationBodyHalfHeightCm);
            return _formationBodyShapeIndex;
        }

        private static int GetBlockerShapeIndex(int halfExtentCm)
        {
            if (BlockerShapeIndicesByHalfExtentCm.TryGetValue(halfExtentCm, out int shapeIndex))
            {
                return shapeIndex;
            }

            shapeIndex = ShapeDataStorage2D.RegisterBox(halfExtentCm, halfExtentCm);
            BlockerShapeIndicesByHalfExtentCm[halfExtentCm] = shapeIndex;
            return shapeIndex;
        }

        private static Rotation2D CreateFormationBodyRotation(Vector2 start, Vector2 goal)
        {
            Vector2 direction = goal - start;
            if (direction.LengthSquared() <= 0.001f)
            {
                return Rotation2D.Identity;
            }

            return Rotation2D.FromRadians(MathF.Atan2(direction.Y, direction.X));
        }

        private static VisualTemplateDefinition ResolveVisualTemplate(Dictionary<string, object>? globals, string key)
        {
            if (globals == null ||
                !globals.TryGetValue(CoreServiceKeys.PresentationVisualTemplateRegistry.Name, out object? registryObject) ||
                registryObject is not VisualTemplateRegistry templates)
            {
                throw new InvalidOperationException("FormationPhysicsPlaygroundMod requires PresentationVisualTemplateRegistry.");
            }

            int templateId = templates.GetId(key);
            if (templateId <= 0 || !templates.TryGet(templateId, out VisualTemplateDefinition template))
            {
                throw new InvalidOperationException($"FormationPhysicsPlaygroundMod references unknown visual template '{key}'.");
            }

            return template;
        }

        private static int AllocateStableId(Dictionary<string, object>? globals)
        {
            if (globals == null ||
                !globals.TryGetValue(CoreServiceKeys.PresentationStableIdAllocator.Name, out object? allocatorObject) ||
                allocatorObject is not PresentationStableIdAllocator allocator)
            {
                throw new InvalidOperationException("FormationPhysicsPlaygroundMod requires PresentationStableIdAllocator.");
            }

            return allocator.Allocate();
        }

        private static void AttachPresentation(World world, Entity entity, Dictionary<string, object>? globals, string templateKey, int? teamId)
        {
            if (globals == null)
            {
                return;
            }

            VisualTemplateDefinition template = ResolveVisualTemplate(globals, templateKey);
            int stableId = AllocateStableId(globals);
            world.Add(entity, new VisualTemplateRef { TemplateId = template.TemplateId });
            world.Add(entity, template.ToRuntimeState());
            world.Add(entity, new PresentationStableId { Value = stableId });
            if (teamId.HasValue)
            {
                world.Add(entity, new Team { Id = teamId.Value + 1 });
            }
        }

        public static void GetGridLayout(int count, out int cols, out int rows)
        {
            if (count <= 0)
            {
                cols = 0;
                rows = 0;
                return;
            }

            cols = (int)Math.Ceiling(Math.Sqrt(count));
            rows = (int)Math.Ceiling(count / (double)cols);
        }

        public static void GetGridCell(int index, int cols, out int row, out int col)
        {
            row = cols <= 0 ? 0 : index / cols;
            col = cols <= 0 ? 0 : index % cols;
        }

        public static int GetCenteredOffset(int index, int count, int spacingCm)
        {
            return count <= 0 ? 0 : -((count - 1) * spacingCm / 2) + index * spacingCm;
        }
    }
}
