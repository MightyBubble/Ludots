using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;

namespace Navigation2DPlaygroundMod.Systems
{
    public readonly record struct Navigation2DPlaygroundSpawnSummary(
        string ScenarioId,
        string ScenarioName,
        int TeamCount,
        int DynamicAgents,
        int BlockerCount);

    public readonly record struct Navigation2DPlaygroundContractSet(
        int NavProfileId,
        int CrowdProfileId,
        int KnockbackPolicyId);

    public static class Navigation2DPlaygroundScenarioSpawner
    {
        private static readonly NavActor PlaygroundNavActor = new()
        {
            IsEnabled = 1,
            PhysicsModeValue = (byte)NavPhysicsMode.NavCrowdResolve,
            DefaultSolverModeValue = (byte)NavSolverMode.Hybrid,
        };

        public static Navigation2DPlaygroundConfig GetPlaygroundConfig(GameConfig? gameConfig)
        {
            if (gameConfig == null)
            {
                throw new InvalidOperationException("Navigation2D playground requires GameConfig.");
            }

            return gameConfig.Navigation2D.CloneValidated().Playground;
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

        public static Navigation2DPlaygroundSpawnSummary SpawnScenario(
            World world,
            Navigation2DContractCatalog catalog,
            Navigation2DPlaygroundConfig playgroundConfig,
            Navigation2DPlaygroundScenarioConfig scenario,
            int agentsPerTeam)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (playgroundConfig == null) throw new ArgumentNullException(nameof(playgroundConfig));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));

            Navigation2DPlaygroundContractSet contracts = ResolveContractSet(catalog, playgroundConfig);
            int dynamicAgents;
            int blockerCount = 0;
            switch (scenario.Kind)
            {
                case Navigation2DPlaygroundScenarioKind.PassThrough:
                    dynamicAgents = SpawnPassThrough(world, scenario, contracts, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.OrthogonalCross:
                    dynamicAgents = SpawnOrthogonalCross(world, scenario, contracts, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.Bottleneck:
                    dynamicAgents = SpawnBottleneck(world, scenario, contracts, agentsPerTeam, out blockerCount);
                    break;
                case Navigation2DPlaygroundScenarioKind.LaneMerge:
                    dynamicAgents = SpawnLaneMerge(world, scenario, contracts, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.CircleSwap:
                    dynamicAgents = SpawnCircleSwap(world, scenario, contracts, agentsPerTeam);
                    break;
                case Navigation2DPlaygroundScenarioKind.GoalQueue:
                    dynamicAgents = SpawnGoalQueue(world, scenario, contracts, agentsPerTeam, out blockerCount);
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
            Navigation2DContractCatalog catalog,
            Navigation2DPlaygroundConfig playgroundConfig,
            int teamId,
            Vector2 centerCm,
            int count,
            int spacingCm,
            int goalRadiusCm)
        {
            Navigation2DPlaygroundContractSet contracts = ResolveContractSet(catalog, playgroundConfig);
            GetGridLayout(count, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < count; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                Vector2 offset = new(
                    GetCenteredOffset(col, cols, spacingCm),
                    GetCenteredOffset(row, rows, spacingCm));
                Vector2 position = centerCm + offset;
                SpawnDynamicAgent(world, contracts, teamId, position, position, goalRadiusCm, flowId: null);
                spawned++;
            }

            return spawned;
        }

        public static int SpawnBlockerBatch(World world, Vector2 centerCm, int count, int spacingCm, int radiusCm)
        {
            GetGridLayout(count, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < count; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int x = (int)MathF.Round(centerCm.X) + GetCenteredOffset(col, cols, spacingCm);
                int y = (int)MathF.Round(centerCm.Y) + GetCenteredOffset(row, rows, spacingCm);
                SpawnBlocker(world, x, y, radiusCm);
                spawned++;
            }

            return spawned;
        }

        public static void ApplyMoveFormation(
            World world,
            NavGroupRuntimeService groups,
            Entity owner,
            ReadOnlySpan<Entity> agents,
            Vector2 targetCm,
            int spacingCm,
            int goalRadiusCm)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));

            Span<Entity> scratch = agents.Length <= 256
                ? stackalloc Entity[agents.Length]
                : new Entity[agents.Length];
            int currentTeamId = int.MinValue;
            int groupCount = 0;

            for (int i = 0; i < agents.Length; i++)
            {
                Entity entity = agents[i];
                if (entity == Entity.Null || !world.IsAlive(entity) || world.Has<NavPlaygroundBlocker>(entity) || !world.Has<Team>(entity))
                {
                    continue;
                }

                int teamId = world.Get<Team>(entity).Id;
                if (currentTeamId == int.MinValue)
                {
                    currentTeamId = teamId;
                }

                if (teamId != currentTeamId)
                {
                    IssueGroupedMove(groups, owner, currentTeamId, scratch.Slice(0, groupCount), targetCm, spacingCm, goalRadiusCm);
                    currentTeamId = teamId;
                    groupCount = 0;
                }

                scratch[groupCount++] = entity;
            }

            if (groupCount > 0 && currentTeamId != int.MinValue)
            {
                IssueGroupedMove(groups, owner, currentTeamId, scratch.Slice(0, groupCount), targetCm, spacingCm, goalRadiusCm);
            }
        }

        private static int SpawnPassThrough(World world, Navigation2DPlaygroundScenarioConfig scenario, Navigation2DPlaygroundContractSet contracts, int agentsPerTeam)
        {
            CreateFlowGoal(world, 0, scenario.GoalOffsetCm, 0, scenario.GoalRadiusCm);
            CreateFlowGoal(world, 1, -scenario.GoalOffsetCm, 0, scenario.GoalRadiusCm);

            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int laneY = GetCenteredOffset(row, rows, scenario.FormationSpacingCm);
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;

                SpawnDynamicAgent(world, contracts, 0, new Vector2(-depth, laneY), new Vector2(scenario.GoalOffsetCm, laneY), scenario.GoalRadiusCm, flowId: 0);
                SpawnDynamicAgent(world, contracts, 1, new Vector2(depth, laneY), new Vector2(-scenario.GoalOffsetCm, laneY), scenario.GoalRadiusCm, flowId: 1);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnOrthogonalCross(World world, Navigation2DPlaygroundScenarioConfig scenario, Navigation2DPlaygroundContractSet contracts, int agentsPerTeam)
        {
            CreateFlowGoal(world, 0, scenario.GoalOffsetCm, 0, scenario.GoalRadiusCm);
            CreateFlowGoal(world, 1, 0, scenario.GoalOffsetCm, scenario.GoalRadiusCm);

            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int lane = GetCenteredOffset(row, rows, scenario.FormationSpacingCm);
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;

                SpawnDynamicAgent(world, contracts, 0, new Vector2(-depth, lane), new Vector2(scenario.GoalOffsetCm, lane), scenario.GoalRadiusCm, flowId: 0);
                SpawnDynamicAgent(world, contracts, 1, new Vector2(lane, -depth), new Vector2(lane, scenario.GoalOffsetCm), scenario.GoalRadiusCm, flowId: 1);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnBottleneck(World world, Navigation2DPlaygroundScenarioConfig scenario, Navigation2DPlaygroundContractSet contracts, int agentsPerTeam, out int blockerCount)
        {
            int spawned = SpawnPassThrough(world, scenario, contracts, agentsPerTeam);
            blockerCount = SpawnVerticalGate(world, scenario.CorridorHalfWidthCm, scenario.BlockerRadiusCm, scenario.BlockerCount, scenario.BlockerSpacingCm);
            return spawned;
        }

        private static int SpawnLaneMerge(World world, Navigation2DPlaygroundScenarioConfig scenario, Navigation2DPlaygroundContractSet contracts, int agentsPerTeam)
        {
            CreateFlowGoal(world, 0, scenario.GoalOffsetCm, 0, scenario.GoalRadiusCm);

            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int lane = GetCenteredOffset(row, rows, scenario.FormationSpacingCm);
                int mergedGoalY = lane / 4;
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;

                SpawnDynamicAgent(world, contracts, 0, new Vector2(-depth, scenario.LaneOffsetCm + lane), new Vector2(scenario.GoalOffsetCm, mergedGoalY), scenario.GoalRadiusCm, flowId: 0);
                SpawnDynamicAgent(world, contracts, 1, new Vector2(-depth, -scenario.LaneOffsetCm + lane), new Vector2(scenario.GoalOffsetCm, mergedGoalY), scenario.GoalRadiusCm, flowId: 0);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnCircleSwap(World world, Navigation2DPlaygroundScenarioConfig scenario, Navigation2DPlaygroundContractSet contracts, int agentsPerTeam)
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
                SpawnDynamicAgent(world, contracts, 0, leftPos, -leftPos, scenario.GoalRadiusCm, flowId: null);
                SpawnDynamicAgent(world, contracts, 1, rightPos, -rightPos, scenario.GoalRadiusCm, flowId: null);
                spawned += 2;
            }

            return spawned;
        }

        private static int SpawnGoalQueue(World world, Navigation2DPlaygroundScenarioConfig scenario, Navigation2DPlaygroundContractSet contracts, int agentsPerTeam, out int blockerCount)
        {
            CreateFlowGoal(world, 0, scenario.GoalOffsetCm, 0, scenario.GoalRadiusCm);

            GetGridLayout(agentsPerTeam, out int cols, out int rows);
            int spawned = 0;
            for (int index = 0; index < agentsPerTeam; index++)
            {
                GetGridCell(index, cols, out int row, out int col);
                int lane = GetCenteredOffset(row, rows, scenario.FormationSpacingCm) / 2;
                int depth = scenario.StartOffsetCm + col * scenario.FormationSpacingCm;
                SpawnDynamicAgent(world, contracts, 0, new Vector2(-depth, lane), new Vector2(scenario.GoalOffsetCm, 0), scenario.GoalRadiusCm, flowId: 0);
                spawned++;
            }

            blockerCount = SpawnHorizontalCorridor(world, scenario.GoalOffsetCm, scenario.CorridorHalfWidthCm, scenario.BlockerRadiusCm, scenario.BlockerCount, scenario.BlockerSpacingCm);
            return spawned;
        }

        private static void SpawnDynamicAgent(World world, Navigation2DPlaygroundContractSet contracts, int teamId, Vector2 start, Vector2 goal, int goalRadiusCm, int? flowId)
        {
            var position = Fix64Vec2.FromVector2(start);
            var goalPosition = Fix64Vec2.FromVector2(goal);
            bool controllable = teamId == 0;

            Entity entity;
            if (flowId.HasValue)
            {
                entity = world.Create(
                    PlaygroundNavActor,
                    new NavProfileRef { ProfileId = contracts.NavProfileId },
                    new NavCrowdProfileRef { ProfileId = contracts.CrowdProfileId },
                    new NavKnockbackPolicyRef { PolicyId = contracts.KnockbackPolicyId },
                    new NavFlowBinding2D { SurfaceId = 0, FlowId = flowId.Value },
                    new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                    new Team { Id = teamId },
                    new WorldPositionCm { Value = position },
                    new PreviousWorldPositionCm { Value = position },
                    VisualTransform.Default,
                    new CullState { IsVisible = true, LOD = LODLevel.High },
                    new NavPlaygroundTeam { Id = (byte)teamId });
            }
            else
            {
                entity = world.Create(
                    PlaygroundNavActor,
                    new NavProfileRef { ProfileId = contracts.NavProfileId },
                    new NavCrowdProfileRef { ProfileId = contracts.CrowdProfileId },
                    new NavKnockbackPolicyRef { PolicyId = contracts.KnockbackPolicyId },
                    new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                    new Team { Id = teamId },
                    new WorldPositionCm { Value = position },
                    new PreviousWorldPositionCm { Value = position },
                    VisualTransform.Default,
                    new CullState { IsVisible = true, LOD = LODLevel.High },
                    new NavPlaygroundTeam { Id = (byte)teamId });
            }

            if (controllable)
            {
                world.Add(entity, new NavPlaygroundControllable());
            }
        }

        private static int SpawnVerticalGate(World world, int corridorHalfWidthCm, int blockerRadiusCm, int blockerCount, int blockerSpacingCm)
        {
            int spawned = 0;
            int ring = 0;
            while (spawned < blockerCount)
            {
                int y = corridorHalfWidthCm + blockerRadiusCm + ring * blockerSpacingCm;
                SpawnBlocker(world, 0, y, blockerRadiusCm);
                spawned++;
                if (spawned < blockerCount)
                {
                    SpawnBlocker(world, 0, -y, blockerRadiusCm);
                    spawned++;
                }

                ring++;
            }

            return spawned;
        }

        private static int SpawnHorizontalCorridor(World world, int goalOffsetCm, int corridorHalfWidthCm, int blockerRadiusCm, int blockerCount, int blockerSpacingCm)
        {
            int spawned = 0;
            int column = 0;
            int wallY = corridorHalfWidthCm + blockerRadiusCm;
            while (spawned < blockerCount)
            {
                int x = goalOffsetCm - blockerRadiusCm - column * blockerSpacingCm;
                SpawnBlocker(world, x, wallY, blockerRadiusCm);
                spawned++;
                if (spawned < blockerCount)
                {
                    SpawnBlocker(world, x, -wallY, blockerRadiusCm);
                    spawned++;
                }

                column++;
            }

            return spawned;
        }

        private static void SpawnBlocker(World world, int x, int y, int radiusCm)
        {
            var position = Fix64Vec2.FromInt(x, y);
            world.Create(
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
                Velocity2D.Zero,
                Mass2D.Static,
                new WorldPositionCm { Value = position },
                new PreviousWorldPositionCm { Value = position },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new NavPlaygroundTeam { Id = byte.MaxValue },
                new NavPlaygroundBlocker());
        }

        private static void CreateFlowGoal(World world, int flowId, int goalX, int goalY, int goalRadiusCm)
        {
            world.Create(new NavFlowGoal2D
            {
                FlowId = flowId,
                GoalCm = Fix64Vec2.FromInt(goalX, goalY),
                RadiusCm = Fix64.FromInt(goalRadiusCm),
            });
        }

        private static Vector2 FromPolar(float radius, float angleRad)
        {
            return new Vector2(MathF.Cos(angleRad) * radius, MathF.Sin(angleRad) * radius);
        }

        private static void IssueGroupedMove(NavGroupRuntimeService groups, Entity owner, int teamId, Span<Entity> members, Vector2 targetCm, int spacingCm, int goalRadiusCm)
        {
            groups.IssueMoveCommand(
                owner,
                members,
                teamId,
                Fix64Vec2.FromVector2(targetCm),
                Fix64.FromInt(goalRadiusCm),
                spacingCm,
                Fix64.Zero);
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

        private static Navigation2DPlaygroundContractSet ResolveContractSet(Navigation2DContractCatalog catalog, Navigation2DPlaygroundConfig playgroundConfig)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (playgroundConfig == null)
            {
                throw new ArgumentNullException(nameof(playgroundConfig));
            }

            ValidatePlaygroundConfig(playgroundConfig);
            return new Navigation2DPlaygroundContractSet(
                catalog.RequireNavProfileId(playgroundConfig.AgentNavProfileId),
                catalog.RequireCrowdProfileId(playgroundConfig.AgentCrowdProfileId),
                catalog.RequireKnockbackPolicyId(playgroundConfig.AgentKnockbackPolicyId));
        }

        private static void ValidatePlaygroundConfig(Navigation2DPlaygroundConfig playgroundConfig)
        {
            if (string.IsNullOrWhiteSpace(playgroundConfig.AgentNavProfileId))
            {
                throw new InvalidOperationException("Navigation2D playground requires Playground.AgentNavProfileId.");
            }

            if (string.IsNullOrWhiteSpace(playgroundConfig.AgentCrowdProfileId))
            {
                throw new InvalidOperationException("Navigation2D playground requires Playground.AgentCrowdProfileId.");
            }

            if (string.IsNullOrWhiteSpace(playgroundConfig.AgentKnockbackPolicyId))
            {
                throw new InvalidOperationException("Navigation2D playground requires Playground.AgentKnockbackPolicyId.");
            }

            if (playgroundConfig.Scenarios.Count <= 0)
            {
                throw new InvalidOperationException("Navigation2D playground requires an explicit Playground.Scenarios catalog.");
            }

            for (int i = 0; i < playgroundConfig.Scenarios.Count; i++)
            {
                Navigation2DPlaygroundScenarioConfig scenario = playgroundConfig.Scenarios[i];
                if (string.IsNullOrWhiteSpace(scenario.Id))
                {
                    throw new InvalidOperationException($"Navigation2D playground scenario #{i} is missing Id.");
                }

                if (string.IsNullOrWhiteSpace(scenario.Name))
                {
                    throw new InvalidOperationException($"Navigation2D playground scenario '{scenario.Id}' is missing Name.");
                }
            }
        }
    }
}

