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

            BuildAgentSoA();
            var agentSoA = _runtime.AgentSoA;
            var positions = agentSoA.Positions.AsSpan();
            _runtime.CellMap.Build(positions);

            ComputeSmartStopFlags();

            float dt = deltaTime > 1e-6f ? deltaTime : 1e-6f;
            float invDt = 1f / dt;

            var config = _runtime.Config.Steering;
            var velocities = agentSoA.Velocities.AsSpan();
            var radii = agentSoA.Radii.AsSpan();
            var maxSpeeds = agentSoA.MaxSpeeds.AsSpan();
            var maxAccels = agentSoA.MaxAccels.AsSpan();
            var neighborDistances = agentSoA.NeighborDistances.AsSpan();
            var timeHorizons = agentSoA.TimeHorizons.AsSpan();
            var maxNeighbors = agentSoA.MaxNeighbors.AsSpan();
            var preferredVelocities = agentSoA.PreferredVelocities.AsSpan();
            var outputForces = agentSoA.OutputForces.AsSpan();
            var outputDesiredVelocities = agentSoA.OutputDesiredVelocities.AsSpan();
            var smartStopFlags = agentSoA.SmartStopFlags.AsSpan();

            Span<int> neighborIdxScratch = stackalloc int[MaxNeighborsHard];
            Span<OrcaSolver2D.Neighbor> orcaNeighborScratch = stackalloc OrcaSolver2D.Neighbor[MaxNeighborsHard];
            Span<SonarSolver2D.Obstacle> sonarObstacleScratch = stackalloc SonarSolver2D.Obstacle[MaxNeighborsHard];
            Span<OrcaSolver2D.OrcaLine> lineScratch = stackalloc OrcaSolver2D.OrcaLine[MaxNeighborsHard];
            Span<OrcaSolver2D.OrcaLine> projectionLineScratch = stackalloc OrcaSolver2D.OrcaLine[OrcaSolver2D.MaxProjectionLines];

            for (int i = 0; i < agentSoA.Count; i++)
            {
                Vector2 pos = positions[i];
                Vector2 vel = velocities[i];
                float radius = radii[i];
                float maxSpeed = maxSpeeds[i];
                float maxAccel = maxAccels[i];
                float neighborDistance = neighborDistances[i];
                float timeHorizon = timeHorizons[i];
                int neighborLimit = GetEffectiveNeighborLimit(maxNeighbors[i], config.QueryBudget.MaxNeighborsPerAgent);
                Vector2 preferred = preferredVelocities[i];

                if (smartStopFlags[i] != 0)
                {
                    outputForces[i] = Vector2.Zero;
                    outputDesiredVelocities[i] = Vector2.Zero;
                    continue;
                }

                int neighborCount = 0;
                if (neighborLimit > 0 && neighborDistance > 0f)
                {
                    neighborCount = _runtime.CellMap.CollectNearestNeighborsBudgeted(
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
                    bool useOrca = ShouldUseOrca(config, velocities, vel, preferred, neighborIdxScratch.Slice(0, neighborCount));
                    if (useOrca)
                    {
                        for (int n = 0; n < neighborCount; n++)
                        {
                            int j = neighborIdxScratch[n];
                            orcaNeighborScratch[n] = new OrcaSolver2D.Neighbor(positions[j], velocities[j], radii[j]);
                        }

                        newVel = OrcaSolver2D.ComputeDesiredVelocity(
                            position: pos,
                            velocity: vel,
                            preferredVelocity: preferred,
                            maxSpeed: maxSpeed,
                            radius: radius,
                            timeHorizon: timeHorizon,
                            deltaTime: dt,
                            neighbors: orcaNeighborScratch.Slice(0, neighborCount),
                            linesScratch: lineScratch,
                            projectionLinesScratch: projectionLineScratch);
                    }
                    else
                    {
                        for (int n = 0; n < neighborCount; n++)
                        {
                            int j = neighborIdxScratch[n];
                            sonarObstacleScratch[n] = new SonarSolver2D.Obstacle(positions[j], velocities[j], radii[j]);
                        }

                        newVel = SonarSolver2D.ComputeDesiredVelocity(
                            position: pos,
                            velocity: vel,
                            preferredVelocity: preferred,
                            maxSpeed: maxSpeed,
                            radius: radius,
                            timeHorizon: timeHorizon,
                            obstacles: sonarObstacleScratch.Slice(0, neighborCount),
                            config: config.Sonar,
                            fallbackToPreferredVelocity: _runtime.Config.Steering.Orca.FallbackToPreferredVelocity);
                    }
                }

                Vector2 accel = (newVel - vel) * invDt;
                float accelLenSq = accel.LengthSquared();
                float maxAccelSq = maxAccel * maxAccel;
                if (accelLenSq > maxAccelSq && accelLenSq > 1e-12f)
                {
                    accel *= maxAccel / MathF.Sqrt(accelLenSq);
                }

                outputForces[i] = accel;
                outputDesiredVelocities[i] = newVel;
            }

            ApplySteeringOutputs();

        }

        private static bool ShouldUseOrca(
            Navigation2DSteeringConfig config,
            ReadOnlySpan<Vector2> velocities,
            Vector2 selfVelocity,
            Vector2 preferredVelocity,
            ReadOnlySpan<int> neighborIndices)
        {
            if (!config.Orca.Enabled)
            {
                return false;
            }

            switch (config.Mode)
            {
                case Navigation2DAvoidanceMode.Orca:
                    return true;
                case Navigation2DAvoidanceMode.Sonar:
                    return false;
                case Navigation2DAvoidanceMode.Hybrid:
                    break;
                default:
                    return false;
            }

            if (!config.Hybrid.Enabled)
            {
                return false;
            }

            if (neighborIndices.Length >= config.Hybrid.DenseNeighborThreshold)
            {
                return true;
            }

            float selfSpeed = selfVelocity.Length();
            if (selfSpeed < config.Hybrid.MinSpeedForOrcaCmPerSec)
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
                if (otherVelocity.LengthSquared() <= 1e-6f)
                {
                    continue;
                }

                float dot = Vector2.Dot(Vector2.Normalize(otherVelocity), referenceDirection);
                if (dot <= config.Hybrid.OpposingVelocityDotThreshold)
                {
                    opposingCount++;
                    if (opposingCount >= config.Hybrid.MinOpposingNeighborsForOrca)
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

        private void BuildAgentSoA()
        {
            _runtime.AgentSoA.Clear();

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

                foreach (var index in chunk)
                {
                    var positionCm = positionsCm[index].Value;
                    var velocityCm = velocitiesCm[index].Linear;
                    var kin = kinematics[index];

                    Vector2 position = positionCm.ToVector2();
                    Vector2 velocity = velocityCm.ToVector2();
                    float maxSpeed = kin.MaxSpeedCmPerSec.ToFloat();
                    Vector2 preferredVelocity = Vector2.Zero;
                    bool hasPreferredVelocity = false;

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
                            goalDistance = Vector2.Distance(goalPosition, position);
                        }
                    }

                    if (hasFlowBinding)
                    {
                        var flow = _runtime.TryGetFlow(flowBindings[index].FlowId);
                        if (flow != null && flow.TrySampleDesiredVelocityCm(positionCm, kin.MaxSpeedCmPerSec, out Fix64Vec2 desiredCm))
                        {
                            preferredVelocity = desiredCm.ToVector2();
                            hasPreferredVelocity = true;
                        }
                    }

                    if (!hasPreferredVelocity && hasPointGoal)
                    {
                        preferredVelocity = ComputeGoalPreferredVelocity(goals[index], position, maxSpeed);
                    }

                    if (!_runtime.AgentSoA.TryAdd(
                        position,
                        velocity,
                        kin.RadiusCm.ToFloat(),
                        maxSpeed,
                        kin.MaxAccelCmPerSec2.ToFloat(),
                        kin.NeighborDistCm.ToFloat(),
                        kin.TimeHorizonSec.ToFloat(),
                        ClampMaxNeighbors(kin.MaxNeighbors),
                        preferredVelocity,
                        hasPointGoal,
                        goalPosition,
                        goalRadius,
                        goalDistance))
                    {
                        return;
                    }
                }
            }
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

            var positions = _runtime.AgentSoA.Positions.AsSpan();
            var velocities = _runtime.AgentSoA.Velocities.AsSpan();
            var goalPositions = _runtime.AgentSoA.GoalPositions.AsSpan();
            var goalRadii = _runtime.AgentSoA.GoalRadii.AsSpan();
            var goalDistances = _runtime.AgentSoA.GoalDistances.AsSpan();
            var hasGoals = _runtime.AgentSoA.HasPointGoals.AsSpan();

            float queryRadius = smartStop.QueryRadiusCm;
            float selfGoalDistanceLimit = smartStop.SelfGoalDistanceLimitCm;
            float goalToleranceSq = smartStop.GoalToleranceCm * smartStop.GoalToleranceCm;
            float neighborArrivalSlack = smartStop.ArrivedSlackCm;
            float stoppedSpeedSq = smartStop.StoppedSpeedThresholdCmPerSec * smartStop.StoppedSpeedThresholdCmPerSec;
            int neighborBudget = Math.Min(MaxNeighborsHard, smartStop.MaxNeighbors);

            Span<int> scratch = stackalloc int[MaxNeighborsHard];
            for (int i = 0; i < positions.Length; i++)
            {
                if (hasGoals[i] == 0 || goalDistances[i] > selfGoalDistanceLimit)
                {
                    continue;
                }

                int neighborCount = _runtime.CellMap.CollectNearestNeighborsBudgeted(
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

        private void ApplySteeringOutputs()
        {
            var outputForces = _runtime.AgentSoA.OutputForces.AsSpan();
            var outputDesiredVelocities = _runtime.AgentSoA.OutputDesiredVelocities.AsSpan();
            int outputIndex = 0;

            foreach (ref var chunk in World.Query(in _agentQuery))
            {
                chunk.GetSpan<ForceInput2D, NavDesiredVelocity2D>(out var forces, out var desiredVelocities);
                foreach (var index in chunk)
                {
                    Vector2 accel = outputForces[outputIndex];
                    Vector2 desiredVelocity = outputDesiredVelocities[outputIndex];
                    forces[index] = new ForceInput2D
                    {
                        Force = Fix64Vec2.FromFloat(accel.X, accel.Y)
                    };
                    desiredVelocities[index] = new NavDesiredVelocity2D
                    {
                        ValueCmPerSec = Fix64Vec2.FromFloat(desiredVelocity.X, desiredVelocity.Y)
                    };
                    outputIndex++;
                }
            }
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

        private static Vector2 ComputeGoalPreferredVelocity(in NavGoal2D goal, in Vector2 position, float maxSpeed)
        {
            if (goal.Kind != NavGoalKind2D.Point || maxSpeed <= 0f)
            {
                return Vector2.Zero;
            }

            Vector2 toGoal = goal.TargetCm.ToVector2() - position;
            float distance = toGoal.Length();
            if (distance <= 1e-4f)
            {
                return Vector2.Zero;
            }

            Vector2 dir = toGoal / distance;
            return dir * maxSpeed;
        }
    }
}
