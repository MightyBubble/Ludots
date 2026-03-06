using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Avoidance;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class Navigation2DSteeringSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _needsForceInput = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavKinematics2D>()
            .WithNone<ForceInput2D>();

        private static readonly QueryDescription _needsDesiredVelocity = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavKinematics2D>()
            .WithNone<NavDesiredVelocity2D>();

        private static readonly QueryDescription _agentQuery = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavKinematics2D, ForceInput2D, NavDesiredVelocity2D>();

        private static readonly QueryDescription _flowGoalQuery = new QueryDescription()
            .WithAll<NavFlowGoal2D>();

        private readonly Navigation2DRuntime _runtime;
        private readonly CommandBuffer _commandBuffer = new();

        private const int MaxNeighborsHard = 64;

        public Navigation2DSteeringSystem2D(World world, Navigation2DRuntime runtime) : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float deltaTime)
        {
            EnsureSteeringOutputs();

            TryApplyFlowGoal();
            if (_runtime.FlowEnabled)
            {
                for (int f = 0; f < _runtime.FlowCount; f++)
                {
                    _runtime.Flows[f].Step(_runtime.FlowIterationsPerTick);
                }
            }

            bool usedSteadyStateSync = TrySteadyStateSyncAgentSoA(out Navigation2DWorldSyncResult syncResult);
            if (!usedSteadyStateSync)
            {
                syncResult = SyncAgentSoA();
            }

            var agentSoA = _runtime.AgentSoA;
            if (agentSoA.Count <= 0)
            {
                return;
            }

            if (syncResult.SpatialDirty)
            {
                if (usedSteadyStateSync)
                {
                    _runtime.CellMap.UpdatePositions(agentSoA.Positions.AsSpan(), agentSoA.SpatialDirtyAgentIndices.AsSpan());
                }
                else
                {
                    _runtime.CellMap.Build(agentSoA.Positions.AsSpan());
                }
            }

            if (syncResult.SmartStopDirty || !_runtime.Config.Steering.SmartStop.Enabled)
            {
                ComputeSmartStopFlags();
            }

            ApplySteering(deltaTime);
        }

        private void ApplySteering(float deltaTime)
        {
            float dt = deltaTime > 1e-6f ? deltaTime : 1e-6f;
            var job = new SteeringChunkJob
            {
                Runtime = _runtime,
                DeltaTime = dt,
                InvDeltaTime = 1f / dt
            };

            if (World.SharedJobScheduler == null)
            {
                foreach (ref var chunk in World.Query(in _agentQuery))
                {
                    job.Execute(ref chunk);
                }

                return;
            }

            World.InlineParallelChunkQuery(in _agentQuery, in job);
        }

        private bool TrySteadyStateSyncAgentSoA(out Navigation2DWorldSyncResult syncResult)
        {
            int liveAgentCount = World.CountEntities(in _agentQuery);
            var agentSoA = _runtime.AgentSoA;
            if (liveAgentCount != agentSoA.Count)
            {
                syncResult = default;
                return false;
            }

            agentSoA.BeginSteadyStateUpdate();
            var job = new SteadyStateSyncChunkJob
            {
                Runtime = _runtime
            };

            if (World.SharedJobScheduler == null)
            {
                foreach (ref var chunk in World.Query(in _agentQuery))
                {
                    job.Execute(ref chunk);
                }
            }
            else
            {
                World.InlineParallelChunkQuery(in _agentQuery, in job);
            }

            if (agentSoA.RequiresFullResync())
            {
                syncResult = default;
                return false;
            }

            syncResult = agentSoA.EndSteadyStateUpdate();
            return true;
        }

        private struct SteadyStateSyncChunkJob : IChunkJob
        {
            public Navigation2DRuntime Runtime;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                ref var entityFirst = ref chunk.Entity(0);
                chunk.GetSpan<Position2D, Velocity2D, NavKinematics2D>(out var positionsCm, out var velocitiesCm, out var kinematics);

                bool hasGoal = chunk.Has<NavGoal2D>();
                Span<NavGoal2D> goals = default;
                if (hasGoal)
                {
                    goals = chunk.GetSpan<NavGoal2D>();
                }

                var agentSoA = Runtime.AgentSoA;
                var entityToAgentIndex = agentSoA.EntityToAgentIndex;
                var positions = agentSoA.Positions.AsSpan();

                foreach (var entityIndex in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, entityIndex);
                    if ((uint)entity.Id >= (uint)entityToAgentIndex.Length)
                    {
                        agentSoA.MarkSteadyStateFallbackRequired();
                        return;
                    }

                    int i = entityToAgentIndex[entity.Id];
                    if ((uint)i >= (uint)positions.Length)
                    {
                        agentSoA.MarkSteadyStateFallbackRequired();
                        return;
                    }

                    var positionCm = positionsCm[entityIndex].Value;
                    var velocityCm = velocitiesCm[entityIndex].Linear;
                    var kin = kinematics[entityIndex];

                    Vector2 position = positionCm.ToVector2();
                    Vector2 velocity = velocityCm.ToVector2();
                    bool hasPointGoal = false;
                    Vector2 goalPosition = Vector2.Zero;
                    float goalRadius = 0f;
                    float goalDistance = 0f;

                    if (hasGoal)
                    {
                        var goal = goals[entityIndex];
                        if (goal.Kind == NavGoalKind2D.Point)
                        {
                            hasPointGoal = true;
                            goalPosition = goal.TargetCm.ToVector2();
                            goalRadius = goal.RadiusCm.ToFloat();

                            Vector2 toGoal = goalPosition - position;
                            float goalDistanceSq = toGoal.LengthSquared();
                            if (goalDistanceSq > 1e-8f)
                            {
                                goalDistance = MathF.Sqrt(goalDistanceSq);
                            }
                        }
                    }

                    agentSoA.UpdateExistingAgent(
                        i,
                        position,
                        velocity,
                        kin.RadiusCm.ToFloat(),
                        hasPointGoal,
                        goalPosition,
                        goalRadius,
                        goalDistance);
                }
            }
        }

        private struct SteeringChunkJob : IChunkJob
        {
            public Navigation2DRuntime Runtime;
            public float DeltaTime;
            public float InvDeltaTime;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                ref var entityFirst = ref chunk.Entity(0);
                chunk.GetSpan<Position2D, Velocity2D, NavKinematics2D, ForceInput2D, NavDesiredVelocity2D>(out var positionsCm, out var velocitiesCm, out var kinematics, out var forces, out var desiredVelocities);

                bool hasGoal = chunk.Has<NavGoal2D>();
                Span<NavGoal2D> goals = default;
                if (hasGoal)
                {
                    goals = chunk.GetSpan<NavGoal2D>();
                }

                bool hasFlowBinding = Runtime.FlowEnabled && chunk.Has<NavFlowBinding2D>();
                Span<NavFlowBinding2D> flowBindings = default;
                if (hasFlowBinding)
                {
                    flowBindings = chunk.GetSpan<NavFlowBinding2D>();
                }

                var agentSoA = Runtime.AgentSoA;
                var config = Runtime.Config.Steering;
                var entityToAgentIndex = agentSoA.EntityToAgentIndex;
                var positions = agentSoA.Positions.AsSpan();
                var velocities = agentSoA.Velocities.AsSpan();
                var radii = agentSoA.Radii.AsSpan();
                var smartStopFlags = agentSoA.SmartStopFlags.AsSpan();

                bool forceOrca = false;
                bool forceSonar = false;
                switch (config.Mode)
                {
                    case Navigation2DAvoidanceMode.Orca:
                        forceOrca = config.Orca.Enabled;
                        forceSonar = !forceOrca;
                        break;
                    case Navigation2DAvoidanceMode.Sonar:
                        forceSonar = true;
                        break;
                    case Navigation2DAvoidanceMode.Hybrid:
                        forceSonar = !config.Orca.Enabled || !config.Hybrid.Enabled;
                        break;
                    default:
                        forceSonar = true;
                        break;
                }

                Span<int> neighborIdxScratch = stackalloc int[MaxNeighborsHard];
                Span<OrcaSolver2D.OrcaLine> lineScratch = stackalloc OrcaSolver2D.OrcaLine[MaxNeighborsHard];
                Span<OrcaSolver2D.OrcaLine> projectionLineScratch = stackalloc OrcaSolver2D.OrcaLine[OrcaSolver2D.MaxProjectionLines];
                Span<SonarSolver2D.Interval> sonarIntervalScratch = stackalloc SonarSolver2D.Interval[SonarSolver2D.MaxIntervals];
                var sonarSolveConfig = SonarSolver2D.SolveConfig.FromConfig(config.Sonar, config.Orca.FallbackToPreferredVelocity);

                foreach (var entityIndex in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, entityIndex);
                    ref var force = ref forces[entityIndex];
                    ref var desiredVelocity = ref desiredVelocities[entityIndex];

                    if ((uint)entity.Id >= (uint)entityToAgentIndex.Length)
                    {
                        force = new ForceInput2D { Force = Fix64Vec2.Zero };
                        desiredVelocity = new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero };
                        continue;
                    }

                    int i = entityToAgentIndex[entity.Id];
                    if ((uint)i >= (uint)positions.Length)
                    {
                        force = new ForceInput2D { Force = Fix64Vec2.Zero };
                        desiredVelocity = new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero };
                        continue;
                    }

                    var positionCm = positionsCm[entityIndex].Value;
                    var velocityCm = velocitiesCm[entityIndex].Linear;
                    var kin = kinematics[entityIndex];

                    Vector2 pos = positionCm.ToVector2();
                    Vector2 vel = velocityCm.ToVector2();
                    float radius = kin.RadiusCm.ToFloat();
                    float maxSpeed = kin.MaxSpeedCmPerSec.ToFloat();
                    float maxAccel = kin.MaxAccelCmPerSec2.ToFloat();
                    float neighborDistance = kin.NeighborDistCm.ToFloat();
                    float timeHorizon = kin.TimeHorizonSec.ToFloat();
                    int neighborLimit = GetEffectiveNeighborLimit(ClampMaxNeighbors(kin.MaxNeighbors), config.QueryBudget.MaxNeighborsPerAgent);
                    Vector2 preferred = Vector2.Zero;

                    if (hasGoal)
                    {
                        var goal = goals[entityIndex];
                        if (goal.Kind == NavGoalKind2D.Point)
                        {
                            Vector2 goalPosition = goal.TargetCm.ToVector2();
                            Vector2 toGoal = goalPosition - pos;
                            float goalDistanceSq = toGoal.LengthSquared();
                            if (goalDistanceSq > 1e-8f && maxSpeed > 0f)
                            {
                                preferred = toGoal * (maxSpeed / MathF.Sqrt(goalDistanceSq));
                            }
                        }
                    }

                    if (hasFlowBinding)
                    {
                        var flow = Runtime.TryGetFlow(flowBindings[entityIndex].FlowId);
                        if (flow != null && flow.TrySampleDesiredVelocityCm(positionCm, kin.MaxSpeedCmPerSec, out Fix64Vec2 desiredCm))
                        {
                            preferred = desiredCm.ToVector2();
                        }
                    }

                    if (smartStopFlags[i] != 0)
                    {
                        force = new ForceInput2D { Force = Fix64Vec2.Zero };
                        desiredVelocity = new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero };
                        continue;
                    }

                    int neighborCount = 0;
                    if (neighborLimit > 0 && neighborDistance > 0f)
                    {
                        neighborCount = Runtime.CellMap.CollectNearestNeighborsBudgeted(
                            selfIndex: i,
                            selfPos: pos,
                            radius: neighborDistance,
                            positions: positions,
                            neighborsOut: neighborIdxScratch.Slice(0, neighborLimit),
                            maxCandidateChecks: config.QueryBudget.MaxCandidateChecksPerAgent);
                    }

                    Vector2 newVel;
                    if (neighborCount <= 0)
                    {
                        newVel = ClampToMaxSpeed(preferred, maxSpeed);
                    }
                    else
                    {
                        bool useOrca = forceOrca || (!forceSonar && ShouldUseOrcaHybrid(config.Hybrid, velocities, vel, preferred, neighborIdxScratch.Slice(0, neighborCount)));
                        if (useOrca)
                        {
                            newVel = OrcaSolver2D.ComputeDesiredVelocity(
                                position: pos,
                                velocity: vel,
                                preferredVelocity: preferred,
                                maxSpeed: maxSpeed,
                                radius: radius,
                                timeHorizon: timeHorizon,
                                deltaTime: DeltaTime,
                                neighborIndices: neighborIdxScratch.Slice(0, neighborCount),
                                neighborPositions: positions,
                                neighborVelocities: velocities,
                                neighborRadii: radii,
                                linesScratch: lineScratch,
                                projectionLinesScratch: projectionLineScratch);
                        }
                        else
                        {
                            newVel = SonarSolver2D.ComputeDesiredVelocity(
                                position: pos,
                                velocity: vel,
                                preferredVelocity: preferred,
                                maxSpeed: maxSpeed,
                                radius: radius,
                                timeHorizon: timeHorizon,
                                obstacleIndices: neighborIdxScratch.Slice(0, neighborCount),
                                obstaclePositions: positions,
                                obstacleVelocities: velocities,
                                obstacleRadii: radii,
                                solveConfig: sonarSolveConfig,
                                intervalScratch: sonarIntervalScratch);
                        }
                    }

                    Vector2 accel = (newVel - vel) * InvDeltaTime;
                    float accelLenSq = accel.LengthSquared();
                    float maxAccelSq = maxAccel * maxAccel;
                    if (accelLenSq > maxAccelSq && accelLenSq > 1e-12f)
                    {
                        accel *= maxAccel / MathF.Sqrt(accelLenSq);
                    }

                    force = new ForceInput2D
                    {
                        Force = Fix64Vec2.FromFloat(accel.X, accel.Y)
                    };
                    desiredVelocity = new NavDesiredVelocity2D
                    {
                        ValueCmPerSec = Fix64Vec2.FromFloat(newVel.X, newVel.Y)
                    };
                }
            }
        }

        private struct SmartStopChunkJob : IChunkJob
        {
            public Navigation2DRuntime Runtime;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                var smartStop = Runtime.Config.Steering.SmartStop;
                if (!smartStop.Enabled || smartStop.QueryRadiusCm <= 0 || smartStop.MaxNeighbors <= 0)
                {
                    return;
                }

                ref var entityFirst = ref chunk.Entity(0);
                var agentSoA = Runtime.AgentSoA;
                var entityToAgentIndex = agentSoA.EntityToAgentIndex;
                var flags = agentSoA.SmartStopFlags.AsSpan();
                var positions = agentSoA.Positions.AsSpan();
                var velocities = agentSoA.Velocities.AsSpan();
                var goalPositions = agentSoA.GoalPositions.AsSpan();
                var goalRadii = agentSoA.GoalRadii.AsSpan();
                var goalDistances = agentSoA.GoalDistances.AsSpan();
                var hasGoals = agentSoA.HasPointGoals.AsSpan();

                float queryRadius = smartStop.QueryRadiusCm;
                float selfGoalDistanceLimit = smartStop.SelfGoalDistanceLimitCm;
                float goalToleranceSq = smartStop.GoalToleranceCm * smartStop.GoalToleranceCm;
                float neighborArrivalSlack = smartStop.ArrivedSlackCm;
                float stoppedSpeedSq = smartStop.StoppedSpeedThresholdCmPerSec * smartStop.StoppedSpeedThresholdCmPerSec;
                int neighborBudget = Math.Min(MaxNeighborsHard, smartStop.MaxNeighbors);

                Span<int> scratch = stackalloc int[MaxNeighborsHard];
                foreach (var entityIndex in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, entityIndex);
                    if ((uint)entity.Id >= (uint)entityToAgentIndex.Length)
                    {
                        continue;
                    }

                    int i = entityToAgentIndex[entity.Id];
                    if ((uint)i >= (uint)positions.Length)
                    {
                        continue;
                    }

                    if (hasGoals[i] == 0 || goalDistances[i] > selfGoalDistanceLimit)
                    {
                        continue;
                    }

                    int neighborCount = Runtime.CellMap.CollectNearestNeighborsBudgeted(
                        selfIndex: i,
                        selfPos: positions[i],
                        radius: queryRadius,
                        positions: positions,
                        neighborsOut: scratch.Slice(0, neighborBudget),
                        maxCandidateChecks: smartStop.MaxNeighbors);

                    for (int n = 0; n < neighborCount; n++)
                    {
                        int j = scratch[n];
                        if (hasGoals[j] == 0)
                        {
                            continue;
                        }

                        if (velocities[j].LengthSquared() > stoppedSpeedSq)
                        {
                            continue;
                        }

                        if (Vector2.DistanceSquared(goalPositions[i], goalPositions[j]) > goalToleranceSq)
                        {
                            continue;
                        }

                        if (goalDistances[j] > goalRadii[j] + neighborArrivalSlack)
                        {
                            continue;
                        }

                        flags[i] = 1;
                        break;
                    }
                }
            }
        }

        private static bool ShouldUseOrcaHybrid(
            Navigation2DHybridAvoidanceConfig config,
            ReadOnlySpan<Vector2> velocities,
            Vector2 selfVelocity,
            Vector2 preferredVelocity,
            ReadOnlySpan<int> neighborIndices)
        {
            if (neighborIndices.Length >= config.DenseNeighborThreshold)
            {
                return true;
            }

            float selfSpeed = selfVelocity.Length();
            if (selfSpeed < config.MinSpeedForOrcaCmPerSec)
            {
                return false;
            }

            Vector2 referenceDirection = preferredVelocity.LengthSquared() > 1e-6f
                ? Vector2.Normalize(preferredVelocity)
                : (selfVelocity.LengthSquared() > 1e-6f ? Vector2.Normalize(selfVelocity) : Vector2.UnitX);

            int opposingCount = 0;
            for (int n = 0; n < neighborIndices.Length; n++)
            {
                Vector2 otherVelocity = velocities[neighborIndices[n]];
                float otherSpeedSq = otherVelocity.LengthSquared();
                if (otherSpeedSq <= 1e-6f)
                {
                    continue;
                }

                float otherInvSpeed = 1f / MathF.Sqrt(otherSpeedSq);
                float dot = Vector2.Dot(otherVelocity * otherInvSpeed, referenceDirection);
                if (dot <= config.OpposingVelocityDotThreshold)
                {
                    opposingCount++;
                    if (opposingCount >= config.MinOpposingNeighborsForOrca)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureSteeringOutputs()
        {
            foreach (ref var chunk in World.Query(in _needsForceInput))
            {
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    _commandBuffer.Add(entity, new ForceInput2D { Force = Fix64Vec2.Zero });
                }
            }

            foreach (ref var chunk in World.Query(in _needsDesiredVelocity))
            {
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    _commandBuffer.Add(entity, new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private Navigation2DWorldSyncResult SyncAgentSoA()
        {
            _runtime.AgentSoA.BeginSync();

            foreach (ref var chunk in World.Query(in _agentQuery))
            {
                var positionsCm = chunk.GetSpan<Position2D>();
                var velocitiesCm = chunk.GetSpan<Velocity2D>();
                var kinematics = chunk.GetSpan<NavKinematics2D>();

                bool hasGoal = chunk.Has<NavGoal2D>();
                Span<NavGoal2D> goals = default;
                if (hasGoal)
                {
                    goals = chunk.GetSpan<NavGoal2D>();
                }

                bool hasFlowBinding = _runtime.FlowEnabled && chunk.Has<NavFlowBinding2D>();
                Span<NavFlowBinding2D> flowBindings = default;
                if (hasFlowBinding)
                {
                    flowBindings = chunk.GetSpan<NavFlowBinding2D>();
                }

                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    var positionCm = positionsCm[index].Value;
                    var velocityCm = velocitiesCm[index].Linear;
                    var kin = kinematics[index];

                    Vector2 position = positionCm.ToVector2();
                    Vector2 velocity = velocityCm.ToVector2();
                    float maxSpeed = kin.MaxSpeedCmPerSec.ToFloat();
                    bool hasPointGoal = false;
                    Vector2 goalPosition = Vector2.Zero;
                    float goalRadius = 0f;
                    float goalDistance = 0f;

                    if (hasGoal)
                    {
                        var goal = goals[index];
                        if (goal.Kind == NavGoalKind2D.Point)
                        {
                            hasPointGoal = true;
                            goalPosition = goal.TargetCm.ToVector2();
                            goalRadius = goal.RadiusCm.ToFloat();

                            Vector2 toGoal = goalPosition - position;
                            float goalDistanceSq = toGoal.LengthSquared();
                            if (goalDistanceSq > 1e-8f)
                            {
                                goalDistance = MathF.Sqrt(goalDistanceSq);
                            }
                        }
                    }

                    if (!_runtime.AgentSoA.SyncAgent(
                        entity.Id,
                        position,
                        velocity,
                        kin.RadiusCm.ToFloat(),
                        hasPointGoal,
                        goalPosition,
                        goalRadius,
                        goalDistance))
                    {
                        return _runtime.AgentSoA.EndSync();
                    }
                }
            }

            return _runtime.AgentSoA.EndSync();
        }

        private void ComputeSmartStopFlags()
        {
            var smartStop = _runtime.Config.Steering.SmartStop;
            var flags = _runtime.AgentSoA.SmartStopFlags.AsSpan();
            flags.Clear();

            if (!smartStop.Enabled || smartStop.QueryRadiusCm <= 0 || smartStop.MaxNeighbors <= 0)
            {
                return;
            }

            var job = new SmartStopChunkJob
            {
                Runtime = _runtime
            };

            if (World.SharedJobScheduler == null)
            {
                foreach (ref var chunk in World.Query(in _agentQuery))
                {
                    job.Execute(ref chunk);
                }

                return;
            }

            World.InlineParallelChunkQuery(in _agentQuery, in job);
        }

        private void TryApplyFlowGoal()
        {
            if (_runtime.FlowCount <= 0) return;

            bool has = false;
            foreach (ref var chunk in World.Query(in _flowGoalQuery))
            {
                var goals = chunk.GetSpan<NavFlowGoal2D>();
                foreach (var index in chunk)
                {
                    var goal = goals[index];
                    var flow = _runtime.TryGetFlow(goal.FlowId);
                    if (flow == null) continue;
                    flow.SetGoalPoint(goal.GoalCm, goal.RadiusCm);
                    has = true;
                }
            }

            if (!has)
            {
                return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ClampMaxNeighbors(int maxNeighbors)
        {
            if (maxNeighbors <= 0)
            {
                return 0;
            }

            return maxNeighbors <= MaxNeighborsHard ? maxNeighbors : MaxNeighborsHard;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetEffectiveNeighborLimit(int perAgentMaxNeighbors, int globalMaxNeighbors)
        {
            int perAgent = ClampMaxNeighbors(perAgentMaxNeighbors);
            if (globalMaxNeighbors <= 0)
            {
                return 0;
            }

            return Math.Min(perAgent, globalMaxNeighbors <= MaxNeighborsHard ? globalMaxNeighbors : MaxNeighborsHard);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 ClampToMaxSpeed(in Vector2 velocity, float maxSpeed)
        {
            float lenSq = velocity.LengthSquared();
            if (lenSq <= maxSpeed * maxSpeed)
            {
                return velocity;
            }

            if (lenSq <= 1e-12f)
            {
                return Vector2.Zero;
            }

            return Vector2.Normalize(velocity) * maxSpeed;
        }

    }
}