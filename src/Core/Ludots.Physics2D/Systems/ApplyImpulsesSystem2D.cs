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
                if (pair.SensorOnly != 0) return;
                if (pair.ContactCount == 0) return;

                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    return;
                }

                bool isASleeping = pair.IsSleepingA != 0;
                bool isBSleeping = pair.IsSleepingB != 0;
                bool writesA = pair.MassA.IsDynamic && !isASleeping;
                bool writesB = pair.MassB.IsDynamic && !isBSleeping;
                if (!writesA && !writesB) return;

                // 全定点数冲量计算
                var normalImpulseVector = pair.Normal * pair.AccumulatedNormalImpulse0;
                var tangent = new Fix64Vec2(-pair.Normal.Y, pair.Normal.X);
                var tangentImpulseVector = tangent * pair.AccumulatedTangentImpulse0;
                var totalImpulse = normalImpulseVector + tangentImpulseVector;

                // Velocity2D 与 BuildPhysicsWorldSystem2D 的快照同合同:缺失视为零速,不回写(授权管线之外的实体可能不带该组件)。
                if (writesA)
                {
                    ref var velocityA = ref World.TryGetRef<Velocity2D>(pair.EntityA, out bool hasVelocityA);
                    if (hasVelocityA)
                    {
                        velocityA.Linear = velocityA.Linear - totalImpulse * pair.MassA.InverseMass;
                    }
                }

                if (writesB)
                {
                    ref var velocityB = ref World.TryGetRef<Velocity2D>(pair.EntityB, out bool hasVelocityB);
                    if (hasVelocityB)
                    {
                        velocityB.Linear = velocityB.Linear + totalImpulse * pair.MassB.InverseMass;
                    }
                }
            }
        }
    }
}
