using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using PresenterTreeOverrideShowcaseMod.Runtime;

namespace PresenterTreeOverrideShowcaseMod.Systems
{
    internal sealed class PresenterTreeOrbitMotionSystem : BaseSystem<World, float>
    {
        private const float DegToRad = MathF.PI / 180f;

        private static readonly QueryDescription OrbitQuery = new QueryDescription()
            .WithAll<PresenterTreeOrbitMotion, WorldPositionCm, FacingDirection>();

        private float _elapsedSeconds;

        public PresenterTreeOrbitMotionSystem(World world)
            : base(world)
        {
        }

        public override void Update(in float dt)
        {
            _elapsedSeconds += dt;
            float elapsed = _elapsedSeconds;

            foreach (ref var chunk in World.Query(in OrbitQuery))
            {
                Span<PresenterTreeOrbitMotion> orbits = chunk.GetSpan<PresenterTreeOrbitMotion>();
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();

                foreach (int index in chunk)
                {
                    ref readonly PresenterTreeOrbitMotion orbit = ref orbits[index];
                    if (orbit.RadiusCm <= 0f || orbit.PeriodSeconds <= 0f)
                    {
                        throw new InvalidOperationException(
                            "PresenterTreeOrbitMotion requires RadiusCm > 0 and PeriodSeconds > 0.");
                    }

                    float angle = (orbit.PhaseDeg * DegToRad) + (elapsed * WorldPlane2D.TwoPi / orbit.PeriodSeconds);
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);
                    positions[index].Value = Fix64Vec2.FromFloat(
                        orbit.CenterXCm + (cos * orbit.RadiusCm),
                        orbit.CenterYCm + (sin * orbit.RadiusCm));
                    facings[index].AngleRad = WorldPlane2D.FacingRadFromDirection(-sin, cos);
                }
            }
        }
    }
}
