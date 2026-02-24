using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Avoidance;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Navigation2D.Spatial;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class Navigation2DSteeringSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _needsForceInput = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavKinematics2D>()
            .WithNone<ForceInput2D>();

        private static readonly QueryDescription _agentQuery = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavKinematics2D, ForceInput2D>();

        private static readonly QueryDescription _flowGoalQuery = new QueryDescription()
            .WithAll<NavFlowGoal2D>();

        private readonly Navigation2DRuntime _runtime;
        private readonly CommandBuffer _commandBuffer = new();

        private const int MaxNeighborsHard = 16;

        public Navigation2DSteeringSystem2D(World world, Navigation2DRuntime runtime) : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        // #region agent log
        private static readonly string _dbgLogPath = @"c:\AIProjects\Ludots\.cursor\debug.log";
        private int _dbgFrameCounter;
        private static void DbgLog(string hypothesisId, string location, string message, string dataJson)
        {
            try { File.AppendAllText(_dbgLogPath, $"{{\"hypothesisId\":\"{hypothesisId}\",\"location\":\"{location}\",\"message\":\"{message}\",\"data\":{dataJson},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n"); } catch { }
        }
        // #endregion

        public override void Update(in float deltaTime)
        {
            EnsureForceInput();

            TryApplyFlowGoal();
            if (_runtime.FlowEnabled)
            {
                for (int f = 0; f < _runtime.FlowCount; f++)
                {
                    _runtime.Flows[f].Step(_runtime.FlowIterationsPerTick);
                }
            }

            BuildAgentSoA();
            _runtime.CellMap.Build(_runtime.AgentSoA.PositionsCm.AsSpan());

            // #region agent log
            _dbgFrameCounter++;
            if (_dbgFrameCounter % 60 == 1) DbgLog("H1", "SteeringSystem.Update", "frame_state", $"{{\"frame\":{_dbgFrameCounter},\"flowEnabled\":{_runtime.FlowEnabled.ToString().ToLower()},\"flowCount\":{_runtime.FlowCount},\"agentCount\":{_runtime.AgentSoA.Count},\"flowItersPerTick\":{_runtime.FlowIterationsPerTick}}}");
            // #endregion

            float dt = deltaTime > 1e-6f ? deltaTime : 1e-6f;

            Span<int> neighborIdxScratch = stackalloc int[MaxNeighborsHard];
            Span<OrcaSolver2D.Neighbor> neighborScratch = stackalloc OrcaSolver2D.Neighbor[MaxNeighborsHard];
            Span<OrcaSolver2D.OrcaLine> lineScratch = stackalloc OrcaSolver2D.OrcaLine[MaxNeighborsHard * 2];

            for (int i = 0; i < _runtime.AgentSoA.Count; i++)
            {
                var e = _runtime.AgentSoA.Entities[i];
                if (!World.IsAlive(e)) continue;

                Fix64Vec2 posCm = _runtime.AgentSoA.PositionsCm[i];
                Fix64Vec2 velCm = _runtime.AgentSoA.VelocitiesCmPerSec[i];
                Fix64 radiusCm = _runtime.AgentSoA.RadiiCm[i];
                Fix64 maxSpeed = _runtime.AgentSoA.MaxSpeedCmPerSec[i];

                Vector2 pos = posCm.ToVector2();
                Vector2 vel = velCm.ToVector2();

                Vector2 preferred = ComputePreferredVelocity(e, posCm, maxSpeed);

                int neighborCount = _runtime.CellMap.CollectNeighbors(
                    selfIndex: i,
                    selfPosCm: posCm,
                    radiusCm: World.Get<NavKinematics2D>(e).NeighborDistCm,
                    positionsCm: _runtime.AgentSoA.PositionsCm.AsSpan(),
                    neighborsOut: neighborIdxScratch);

                int used = 0;
                for (int n = 0; n < neighborCount && used < MaxNeighborsHard; n++)
                {
                    int j = neighborIdxScratch[n];
                    var op = _runtime.AgentSoA.PositionsCm[j].ToVector2();
                    var ov = _runtime.AgentSoA.VelocitiesCmPerSec[j].ToVector2();
                    float or = _runtime.AgentSoA.RadiiCm[j].ToFloat();
                    neighborScratch[used++] = new OrcaSolver2D.Neighbor(op, ov, or);
                }

                float maxSpeedF = maxSpeed.ToFloat();
                float timeHorizon = World.Get<NavKinematics2D>(e).TimeHorizonSec.ToFloat();
                float selfRadius = radiusCm.ToFloat();

                Vector2 newVel = OrcaSolver2D.ComputeDesiredVelocity(
                    position: pos,
                    velocity: vel,
                    preferredVelocity: preferred,
                    maxSpeed: maxSpeedF,
                    radius: selfRadius,
                    timeHorizon: timeHorizon,
                    deltaTime: dt,
                    neighbors: neighborScratch.Slice(0, used),
                    linesScratch: lineScratch);

                Vector2 accel = (newVel - vel) / dt;
                Fix64 maxAccel = World.Get<NavKinematics2D>(e).MaxAccelCmPerSec2;
                float maxAccelF = maxAccel.ToFloat();
                float accelLen = accel.Length();
                if (accelLen > maxAccelF && accelLen > 1e-6f)
                {
                    accel = accel / accelLen * maxAccelF;
                }

                World.Set(e, new ForceInput2D { Force = Fix64Vec2.FromFloat(accel.X, accel.Y) });
                World.Set(e, new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromFloat(newVel.X, newVel.Y) });
            }
        }

        private void EnsureForceInput()
        {
            foreach (ref var chunk in World.Query(in _needsForceInput))
            {
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    _commandBuffer.Add(entity, new ForceInput2D { Force = Fix64Vec2.Zero });
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
                ref var entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<Position2D>();
                var velocities = chunk.GetSpan<Velocity2D>();
                var kins = chunk.GetSpan<NavKinematics2D>();

                foreach (var index in chunk)
                {
                    if (_runtime.AgentSoA.Count >= _runtime.AgentSoA.Settings.MaxAgents) return;

                    var entity = Unsafe.Add(ref entityFirst, index);
                    _runtime.AgentSoA.Entities.Add(entity);
                    _runtime.AgentSoA.PositionsCm.Add(positions[index].Value);
                    _runtime.AgentSoA.VelocitiesCmPerSec.Add(velocities[index].Linear);
                    _runtime.AgentSoA.RadiiCm.Add(kins[index].RadiusCm);
                    _runtime.AgentSoA.MaxSpeedCmPerSec.Add(kins[index].MaxSpeedCmPerSec);
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

        // #region agent log
        private int _dbgPrefVelSampleCounter;
        // #endregion

        private Vector2 ComputePreferredVelocity(Entity e, Fix64Vec2 posCm, Fix64 maxSpeedCmPerSec)
        {
            // #region agent log
            bool dbgSample = (_dbgFrameCounter % 60 == 1) && (_dbgPrefVelSampleCounter++ < 3);
            // #endregion

            if (_runtime.FlowEnabled && World.TryGet(e, out NavFlowBinding2D binding))
            {
                var flow = _runtime.TryGetFlow(binding.FlowId);
                if (flow != null && flow.TrySampleDesiredVelocityCm(posCm, maxSpeedCmPerSec, out Fix64Vec2 desired))
                {
                    // #region agent log
                    if (dbgSample) DbgLog("H1,H3", "ComputePreferredVelocity", "flow_path_used", $"{{\"entityId\":{e.Id},\"flowId\":{binding.FlowId},\"posCm\":\"{posCm.X.ToFloat():F1},{posCm.Y.ToFloat():F1}\",\"desired\":\"{desired.X.ToFloat():F1},{desired.Y.ToFloat():F1}\"}}");
                    // #endregion
                    return desired.ToVector2();
                }
                else
                {
                    // #region agent log
                    if (dbgSample) DbgLog("H1,H2", "ComputePreferredVelocity", "flow_sample_failed", $"{{\"entityId\":{e.Id},\"flowId\":{binding.FlowId},\"flowNull\":{(flow == null).ToString().ToLower()},\"posCm\":\"{posCm.X.ToFloat():F1},{posCm.Y.ToFloat():F1}\"}}");
                    // #endregion
                }
            }
            else
            {
                // #region agent log
                if (dbgSample) DbgLog("H1", "ComputePreferredVelocity", "flow_disabled_or_no_binding", $"{{\"entityId\":{e.Id},\"flowEnabled\":{_runtime.FlowEnabled.ToString().ToLower()},\"hasBinding\":{World.Has<NavFlowBinding2D>(e).ToString().ToLower()}}}");
                // #endregion
            }

            if (World.TryGet(e, out NavGoal2D goal) && goal.Kind == NavGoalKind2D.Point)
            {
                Vector2 delta = (goal.TargetCm - posCm).ToVector2();
                float len = delta.Length();
                if (len > 1e-6f)
                {
                    // #region agent log
                    if (dbgSample) { var dir = delta / len; DbgLog("H1", "ComputePreferredVelocity", "goal_fallback_used", $"{{\"entityId\":{e.Id},\"dir\":\"{dir.X:F3},{dir.Y:F3}\"}}"); }
                    // #endregion
                    return delta / len * maxSpeedCmPerSec.ToFloat();
                }
            }

            return Vector2.Zero;
        }
    }
}
