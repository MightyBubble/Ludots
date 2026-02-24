using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 冲量应用系统 — 全定点数域，将求解器结果写回实体速度。
    /// </summary>
    public sealed class ApplyImpulsesSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _pairsQuery =
            new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();

        public ApplyImpulsesSystem2D(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
            var job = new ApplyImpulsesJob { World = World };
            World.InlineQuery<ApplyImpulsesJob, CollisionPair>(in _pairsQuery, ref job);
        }

        private struct ApplyImpulsesJob : IForEach<CollisionPair>
        {
            public World World;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (pair.ContactCount == 0) return;

                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    return;
                }

                bool isASleeping = World.Has<SleepingTag>(pair.EntityA);
                bool isBSleeping = World.Has<SleepingTag>(pair.EntityB);
                if (isASleeping && isBSleeping) return;

                ref var velocityA = ref pair.EntityA.Get<Velocity2D>();
                ref var velocityB = ref pair.EntityB.Get<Velocity2D>();

                // 全定点数冲量计算
                var normalImpulseVector = pair.Normal * pair.AccumulatedNormalImpulse0;
                var tangent = new Fix64Vec2(-pair.Normal.Y, pair.Normal.X);
                var tangentImpulseVector = tangent * pair.AccumulatedTangentImpulse0;
                var totalImpulse = normalImpulseVector + tangentImpulseVector;

                if (pair.MassA.IsDynamic && !isASleeping)
                {
                    velocityA.Linear = velocityA.Linear - totalImpulse * pair.MassA.InverseMass;
                }

                if (pair.MassB.IsDynamic && !isBSleeping)
                {
                    velocityB.Linear = velocityB.Linear + totalImpulse * pair.MassB.InverseMass;
                }
            }
        }
    }
}
