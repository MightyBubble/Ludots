using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using PerformerBlacksmithShowcaseMod.Runtime;

namespace PerformerBlacksmithShowcaseMod.Systems
{
    internal sealed class DynamicWorkerCrowdMovementSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription WorkerQuery = new QueryDescription()
            .WithAll<DynamicWorkerCrowdTag, WorldPositionCm, PreviousWorldPositionCm, FacingDirection>();

        private float _elapsedSeconds;

        public DynamicWorkerCrowdMovementSystem(World world)
            : base(world)
        {
        }

        public override void Update(in float dt)
        {
            _elapsedSeconds += dt;
            float elapsed = _elapsedSeconds;

            foreach (ref var chunk in World.Query(in WorkerQuery))
            {
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<PreviousWorldPositionCm> previousPositions = chunk.GetSpan<PreviousWorldPositionCm>();
                Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();

                foreach (int index in chunk)
                {
                    ref WorldPositionCm position = ref positions[index];
                    previousPositions[index] = new PreviousWorldPositionCm { Value = position.Value };

                    float x = position.Value.X.ToFloat();
                    float y = position.Value.Y.ToFloat();
                    float phase = ((x * 0.0017f) + (y * 0.0023f)) % (MathF.PI * 2f);
                    float angle = elapsed * 0.85f + phase;
                    float vx = MathF.Cos(angle) * 42f;
                    float vy = MathF.Sin(angle) * 42f;
                    position.Value = Fix64Vec2.FromFloat(x + vx * dt, y + vy * dt);
                    facings[index].AngleRad = MathF.Atan2(vy, vx);
                }
            }
        }
    }
}
