using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 位置修正系统 — 全定点数域，修正穿透物体位置。
    /// </summary>
    public sealed class PositionCorrectionSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _pairsQuery;
        private readonly Physics2DSolverConfig _config;

        public PositionCorrectionSystem2D(World world, Physics2DSolverConfig config) : base(world)
        {
            _pairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override void Update(in float deltaTime)
        {
            var job = new PositionCorrectionJob
            {
                World = World,
                CorrectionPercentage = _config.PositionCorrectionPercentageFix64,
                Slop = _config.PositionCorrectionSlopFix64,
                Epsilon = _config.EpsilonFix64
            };
            World.InlineQuery<PositionCorrectionJob, CollisionPair>(in _pairsQuery, ref job);
        }

        private struct PositionCorrectionJob : IForEach<CollisionPair>
        {
            public World World;
            public Fix64 CorrectionPercentage;
            public Fix64 Slop;
            public Fix64 Epsilon;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (pair.ContactCount == 0) return;

                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    return;
                }

                if (pair.Penetration <= Slop) return;

                ref var massA = ref pair.EntityA.Get<Mass2D>();
                ref var massB = ref pair.EntityB.Get<Mass2D>();
                Fix64 effectivePenetration = pair.Penetration - Slop;
                Fix64 correctionAmount = effectivePenetration * CorrectionPercentage;

                Fix64 invMassA = pair.IsSleepingA != 0 ? Fix64.Zero : pair.MassA.InverseMass;
                Fix64 invMassB = pair.IsSleepingB != 0 ? Fix64.Zero : pair.MassB.InverseMass;

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
            }
        }
    }
}
