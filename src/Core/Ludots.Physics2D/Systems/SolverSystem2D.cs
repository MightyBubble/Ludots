using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Utils;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 碰撞求解器系统 — 全定点数域迭代求解碰撞冲量。
    /// 确保跨平台确定性。
    /// </summary>
    public sealed class SolverSystem2D : BaseSystem<World, float>
    {
        private const int NumIterations = 6;
        private static readonly Fix64 Epsilon = Fix64.FromFloat(0.000001f);

        private readonly QueryDescription _pairsQuery;

        public SolverSystem2D(World world) : base(world)
        {
            _pairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        }

        public override void Update(in float deltaTime)
        {
            // Phase 1: 复制速度快照到 CollisionPair 并计算组合材质
            World.Query(in _pairsQuery, (ref CollisionPair pair) =>
            {
                if (pair.ContactCount == 0)
                {
                    pair.AccumulatedNormalImpulse0 = Fix64.Zero;
                    pair.AccumulatedTangentImpulse0 = Fix64.Zero;
                    pair.AccumulatedNormalImpulse1 = Fix64.Zero;
                    pair.AccumulatedTangentImpulse1 = Fix64.Zero;
                    return;
                }

                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    return;
                }

                ref var velocityA = ref pair.EntityA.Get<Velocity2D>();
                ref var velocityB = ref pair.EntityB.Get<Velocity2D>();
                ref var massA = ref pair.EntityA.Get<Mass2D>();
                ref var massB = ref pair.EntityB.Get<Mass2D>();

                pair.VelocityA = velocityA;
                pair.VelocityB = velocityB;
                pair.MassA = massA;
                pair.MassB = massB;

                var materialA = World.TryGet(pair.EntityA, out PhysicsMaterial2D matA) ? matA : PhysicsMaterial2D.Default;
                var materialB = World.TryGet(pair.EntityB, out PhysicsMaterial2D matB) ? matB : PhysicsMaterial2D.Default;

                pair.CombinedFriction = MaterialCombiner.CombineFriction(materialA.Friction, materialB.Friction);
                pair.CombinedRestitution = MaterialCombiner.CombineRestitution(materialA.Restitution, materialB.Restitution);

                ApplyWarmStart(ref pair);
            });

            // Phase 2: 迭代求解
            for (int iteration = 0; iteration < NumIterations; iteration++)
            {
                World.Query(in _pairsQuery, (ref CollisionPair pair) =>
                {
                    if (pair.ContactCount == 0) return;

                    SolveContact0(ref pair);
                });
            }
        }

        private static void ApplyWarmStart(ref CollisionPair pair)
        {
            if (pair.AccumulatedNormalImpulse0 == Fix64.Zero && pair.AccumulatedTangentImpulse0 == Fix64.Zero)
            {
                return;
            }

            ApplyImpulseToSnapshot(ref pair, pair.AccumulatedNormalImpulse0, pair.AccumulatedTangentImpulse0);
        }

        private static void SolveContact0(ref CollisionPair pair)
        {
            var relativeVelocity = pair.VelocityB.Linear - pair.VelocityA.Linear;
            Fix64 relativeNormalVelocity = Fix64Vec2.Dot(relativeVelocity, pair.Normal);

            if (relativeNormalVelocity >= Fix64.Zero) return;

            Fix64 effectiveMass = pair.MassA.InverseMass + pair.MassB.InverseMass;
            if (effectiveMass < Epsilon) return;

            Fix64 normalImpulse = -(Fix64.OneValue + pair.CombinedRestitution) * relativeNormalVelocity / effectiveMass;
            normalImpulse = Fix64.Max(normalImpulse, Fix64.Zero);

            Fix64 oldNormal = pair.AccumulatedNormalImpulse0;
            Fix64 newNormal = Fix64.Max(oldNormal + normalImpulse, Fix64.Zero);
            Fix64 deltaNormal = newNormal - oldNormal;
            pair.AccumulatedNormalImpulse0 = newNormal;

            var tangent = new Fix64Vec2(-pair.Normal.Y, pair.Normal.X);
            Fix64 relativeTangentVelocity = Fix64Vec2.Dot(relativeVelocity, tangent);

            Fix64 tangentImpulse = Fix64.Zero;
            if (Fix64.Abs(relativeTangentVelocity) >= Epsilon)
            {
                tangentImpulse = -relativeTangentVelocity / effectiveMass;
            }

            Fix64 oldTangent = pair.AccumulatedTangentImpulse0;
            Fix64 maxFriction = pair.CombinedFriction * newNormal;
            Fix64 newTangent = Fix64.Clamp(oldTangent + tangentImpulse, -maxFriction, maxFriction);
            Fix64 deltaTangent = newTangent - oldTangent;
            pair.AccumulatedTangentImpulse0 = newTangent;

            ApplyImpulseToSnapshot(ref pair, deltaNormal, deltaTangent);
        }

        private static void ApplyImpulseToSnapshot(ref CollisionPair pair, Fix64 normalImpulse, Fix64 tangentImpulse)
        {
            var normalImpulseVector = pair.Normal * normalImpulse;
            var tangent = new Fix64Vec2(-pair.Normal.Y, pair.Normal.X);
            var tangentImpulseVector = tangent * tangentImpulse;
            var totalImpulse = normalImpulseVector + tangentImpulseVector;

            pair.VelocityA.Linear = pair.VelocityA.Linear - totalImpulse * pair.MassA.InverseMass;
            pair.VelocityB.Linear = pair.VelocityB.Linear + totalImpulse * pair.MassB.InverseMass;
        }
    }
}
