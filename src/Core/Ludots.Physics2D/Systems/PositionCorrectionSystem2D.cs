using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 位置修正系统 — 全定点数域，修正穿透物体位置。
    /// </summary>
    public sealed class PositionCorrectionSystem2D : BaseSystem<World, float>
    {
        private static readonly Fix64 CorrectionPercentage = Fix64.FromFloat(0.4f);
        private static readonly Fix64 Slop = Fix64.FromFloat(0.01f);
        private static readonly Fix64 Epsilon = Fix64.FromFloat(0.000001f);

        private readonly QueryDescription _pairsQuery;

        public PositionCorrectionSystem2D(World world) : base(world)
        {
            _pairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        }

        public override void Update(in float deltaTime)
        {
            World.Query(in _pairsQuery, (ref CollisionPair pair) =>
            {
                if (pair.ContactCount == 0) return;

                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    return;
                }

                if (pair.Penetration <= Slop) return;

                Fix64 effectivePenetration = pair.Penetration - Slop;
                Fix64 correctionAmount = effectivePenetration * CorrectionPercentage;

                Fix64 invMassA = World.Has<SleepingTag>(pair.EntityA) ? Fix64.Zero : pair.MassA.InverseMass;
                Fix64 invMassB = World.Has<SleepingTag>(pair.EntityB) ? Fix64.Zero : pair.MassB.InverseMass;

                Fix64 totalInverseMass = invMassA + invMassB;
                if (totalInverseMass < Epsilon) return;

                Fix64Vec2 correction = pair.Normal * (correctionAmount / totalInverseMass);

                ref var positionA = ref pair.EntityA.Get<Position2D>();
                ref var positionB = ref pair.EntityB.Get<Position2D>();

                if (invMassA > Fix64.Zero)
                {
                    positionA.Value = positionA.Value - correction * invMassA;
                }

                if (invMassB > Fix64.Zero)
                {
                    positionB.Value = positionB.Value + correction * invMassB;
                }
            });
        }
    }
}
