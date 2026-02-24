using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ProjectileRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription().WithAll<ProjectileState, WorldPositionCm>();
        private readonly EffectRequestQueue _effectRequests;

        public ProjectileRuntimeSystem(World world, IClock clock, EffectRequestQueue effectRequests) : base(world)
        {
            _effectRequests = effectRequests;
        }

        public override void Update(in float dt)
        {
            if (_effectRequests == null) return;

            float deltaTime = dt; // Copy to local — 'in' params can't be captured in lambdas
            World.Query(in _query, (Entity e, ref ProjectileState ps, ref WorldPositionCm pos) =>
            {
                if (!World.IsAlive(ps.Source))
                {
                    World.Destroy(e);
                    return;
                }

                if (ps.Speed <= 0 || ps.Range <= 0)
                {
                    World.Destroy(e);
                    return;
                }

                float stepCm = ps.Speed * deltaTime;
                if (stepCm <= 0f) return;

                Fix64Vec2 current = pos.Value;
                Fix64Vec2 next = current;

                if (World.IsAlive(ps.Target) && World.Has<WorldPositionCm>(ps.Target))
                {
                    var targetPos = World.Get<WorldPositionCm>(ps.Target).Value;
                    var delta = targetPos - current;
                    float dx = delta.X.ToFloat();
                    float dy = delta.Y.ToFloat();
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist <= stepCm || dist <= 1f)
                    {
                        next = targetPos;
                        Hit(e, ref ps, next);
                        return;
                    }

                    float inv = 1f / dist;
                    float ux = dx * inv;
                    float uy = dy * inv;
                    next = current + Fix64Vec2.FromFloat(ux * stepCm, uy * stepCm);
                }
                else
                {
                    next = current + Fix64Vec2.FromFloat(stepCm, 0f);
                }

                ps.TraveledCm += (int)MathF.Round(stepCm);
                pos.Value = next;

                if (ps.TraveledCm >= ps.Range)
                {
                    if (ps.ImpactEffectTemplateId > 0 && World.IsAlive(ps.Target))
                    {
                        _effectRequests.Publish(new EffectRequest
                        {
                            RootId = 0,
                            Source = ps.Source,
                            Target = ps.Target,
                            TargetContext = default,
                            TemplateId = ps.ImpactEffectTemplateId
                        });
                    }
                    World.Destroy(e);
                }
            });
        }

        private void Hit(Entity projectile, ref ProjectileState ps, Fix64Vec2 hitPos)
        {
            if (ps.ImpactEffectTemplateId > 0 && World.IsAlive(ps.Target))
            {
                _effectRequests.Publish(new EffectRequest
                {
                    RootId = 0,
                    Source = ps.Source,
                    Target = ps.Target,
                    TargetContext = default,
                    TemplateId = ps.ImpactEffectTemplateId
                });
            }
            World.Destroy(projectile);
        }
    }
}
