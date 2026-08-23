using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 场检测系统 — 全定点数域，检测实体是否在阻尼场内。
    /// </summary>
    public sealed class FieldDetectorSystem : BaseSystem<World, float>
    {
        private readonly QueryDescription _dampingFieldQuery;
        private readonly QueryDescription _dynamicEntitiesWithDampingQuery;
        private readonly QueryDescription _dynamicEntitiesWithoutDampingQuery;

        private readonly struct CachedField
        {
            public readonly Fix64Vec2 Position;
            public readonly Fix64 RadiusSq;
            public readonly Fix64 DampingValue;

            public CachedField(Fix64Vec2 position, Fix64 radius, Fix64 dampingValue)
            {
                Position = position;
                RadiusSq = radius * radius;
                DampingValue = dampingValue;
            }
        }

        private readonly List<CachedField> _cachedFields = new(16);

        public FieldDetectorSystem(World world) : base(world)
        {
            _dampingFieldQuery = new QueryDescription().WithAll<Position2D, DampingField>();
            _dynamicEntitiesWithDampingQuery = new QueryDescription().WithAll<Position2D, Velocity2D, Mass2D, AppliedDamping>();
            _dynamicEntitiesWithoutDampingQuery = new QueryDescription().WithAll<Position2D, Velocity2D, Mass2D>().WithNone<AppliedDamping>();
        }

        public override void Update(in float deltaTime)
        {
            _cachedFields.Clear();
            var collectJob = new CollectFieldsJob { CachedFields = _cachedFields };
            World.InlineQuery<CollectFieldsJob, Position2D, DampingField>(in _dampingFieldQuery, ref collectJob);

            if (_cachedFields.Count == 0)
            {
                var resetJob = new ResetDampingJob();
                World.InlineQuery<ResetDampingJob, AppliedDamping>(in _dynamicEntitiesWithDampingQuery, ref resetJob);
                return;
            }

            var applyJob = new ApplyDampingJob { CachedFields = _cachedFields };
            World.InlineQuery<ApplyDampingJob, Position2D, Mass2D, AppliedDamping>(in _dynamicEntitiesWithDampingQuery, ref applyJob);

            var addJob = new AddDampingJob { World = World, CachedFields = _cachedFields };
            World.InlineEntityQuery<AddDampingJob, Position2D, Mass2D>(in _dynamicEntitiesWithoutDampingQuery, ref addJob);
        }

        private struct CollectFieldsJob : IForEach<Position2D, DampingField>
        {
            public List<CachedField> CachedFields;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Position2D position, ref DampingField field)
            {
                if (field.Radius > Fix64.Zero && field.DampingValue > Fix64.Zero && field.DampingValue <= Fix64.OneValue)
                {
                    CachedFields.Add(new CachedField(position.Value, field.Radius, field.DampingValue));
                }
            }
        }

        private struct ResetDampingJob : IForEach<AppliedDamping>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref AppliedDamping damping)
            {
                damping.TotalFieldDamping = Fix64.OneValue;
            }
        }

        private struct ApplyDampingJob : IForEach<Position2D, Mass2D, AppliedDamping>
        {
            public List<CachedField> CachedFields;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Position2D position, ref Mass2D mass, ref AppliedDamping damping)
            {
                // Only dynamic bodies experience field damping; kinematic velocity is pose-derived.
                if (!mass.IsDynamic)
                {
                    damping.TotalFieldDamping = Fix64.OneValue;
                    return;
                }

                damping.TotalFieldDamping = Fix64.OneValue;
                for (int i = 0; i < CachedFields.Count; i++)
                {
                    var field = CachedFields[i];
                    Fix64 distanceSq = Fix64Vec2.DistanceSquared(position.Value, field.Position);
                    if (distanceSq <= field.RadiusSq)
                    {
                        damping.TotalFieldDamping = damping.TotalFieldDamping * field.DampingValue;
                    }
                }
            }
        }

        private struct AddDampingJob : IForEachWithEntity<Position2D, Mass2D>
        {
            public World World;
            public List<CachedField> CachedFields;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Position2D position, ref Mass2D mass)
            {
                if (!mass.IsDynamic) return;

                Fix64 totalFieldDamping = Fix64.OneValue;
                bool inAnyField = false;

                for (int i = 0; i < CachedFields.Count; i++)
                {
                    var field = CachedFields[i];
                    Fix64 distanceSq = Fix64Vec2.DistanceSquared(position.Value, field.Position);
                    if (distanceSq <= field.RadiusSq)
                    {
                        totalFieldDamping = totalFieldDamping * field.DampingValue;
                        inAnyField = true;
                    }
                }

                if (inAnyField)
                {
                    World.Add(entity, new AppliedDamping { TotalFieldDamping = totalFieldDamping });
                }
            }
        }
    }
}
