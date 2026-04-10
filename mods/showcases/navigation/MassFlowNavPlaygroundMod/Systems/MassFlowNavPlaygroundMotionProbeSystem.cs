using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundMotionProbeSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private int _lastProbeEntityId = -1;
        private string _lastLoggedLine = string.Empty;

        public MassFlowNavPlaygroundMotionProbeSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(_engine.CurrentMapSession?.MapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Entity probe = state.MotionProbeEntity;
            if (probe == Entity.Null || state.MotionProbeFramesRemaining <= 0)
            {
                return;
            }

            string line = BuildProbeLine(_engine.World, probe, state.MotionProbeFramesRemaining);
            state.LastMotionProbeDebug = line;

            bool probeChanged = _lastProbeEntityId != probe.Id;
            bool shouldLog = probeChanged || state.MotionProbeFramesRemaining >= 179 || (state.MotionProbeFramesRemaining % 15) == 0;
            if (shouldLog && !string.Equals(_lastLoggedLine, line, StringComparison.Ordinal))
            {
                Log.Info(in LogChannels.Input, $"[MassFlowNav] {line}");
                _lastLoggedLine = line;
                _lastProbeEntityId = probe.Id;
            }

            state.AdvanceMotionProbe();
        }

        private static string BuildProbeLine(World world, Entity entity, int framesRemaining)
        {
            if (!world.IsAlive(entity))
            {
                return $"probe #{entity.Id} dead frames={framesRemaining}";
            }

            bool hasGoal = world.TryGet(entity, out NavGoal2D goal) && goal.Kind == NavGoalKind2D.Point;
            bool hasForce = world.TryGet(entity, out ForceInput2D force);
            bool hasDesired = world.TryGet(entity, out NavDesiredVelocity2D desired);
            bool hasVelocity = world.TryGet(entity, out Velocity2D velocity);
            bool hasPosition = world.TryGet(entity, out Position2D position);
            bool hasWorldPosition = world.TryGet(entity, out WorldPositionCm worldPosition);
            bool hasPrevious = world.TryGet(entity, out PreviousPosition2D previousPosition);
            bool sleeping = world.Has<SleepingTag>(entity);
            byte smartStopSuppressed = world.TryGet(entity, out NavAgent2D agent) ? agent.SmartStopSuppressed : (byte)0;

            float goalDistance = -1f;
            if (hasGoal && hasPosition)
            {
                Vector2 delta = goal.TargetCm.ToVector2() - position.Value.ToVector2();
                goalDistance = delta.Length();
            }

            return $"probe #{entity.Id} f={framesRemaining} goal={Bool01(hasGoal)} dist={FormatFloat(goalDistance)} sup={smartStopSuppressed} sleep={Bool01(sleeping)} desired={FormatFix64Vec2(hasDesired ? desired.ValueCmPerSec : Fix64Vec2.Zero, hasDesired)} force={FormatFix64Vec2(hasForce ? force.Force : Fix64Vec2.Zero, hasForce)} vel={FormatFix64Vec2(hasVelocity ? velocity.Linear : Fix64Vec2.Zero, hasVelocity)} pos={FormatFix64Vec2(hasPosition ? position.Value : Fix64Vec2.Zero, hasPosition)} prev={FormatFix64Vec2(hasPrevious ? previousPosition.Value : Fix64Vec2.Zero, hasPrevious)} world={FormatFix64Vec2(hasWorldPosition ? worldPosition.Value : Fix64Vec2.Zero, hasWorldPosition)}";
        }

        private static string Bool01(bool value)
        {
            return value ? "1" : "0";
        }

        private static string FormatFloat(float value)
        {
            return value < 0f ? "-" : MathF.Round(value).ToString("0");
        }

        private static string FormatFix64Vec2(Fix64Vec2 value, bool available)
        {
            if (!available)
            {
                return "-";
            }

            Vector2 v = value.ToVector2();
            return $"({MathF.Round(v.X):0},{MathF.Round(v.Y):0})";
        }
    }
}
