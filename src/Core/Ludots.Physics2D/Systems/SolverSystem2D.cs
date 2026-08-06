using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
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
        private readonly QueryDescription _pairsQuery;
        private readonly Physics2DSolverConfig _config;

        public SolverSystem2D(World world, Physics2DSolverConfig config) : base(world)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _pairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        }

        public override void Update(in float deltaTime)
        {
            // Phase 1: 复制速度快照到 CollisionPair 并计算组合材质
            var prepareJob = new PrepareSolverPairsJob
            {
                World = World,
                DefaultFriction = _config.DefaultFrictionFix64,
                DefaultRestitution = _config.DefaultRestitutionFix64
            };
            World.InlineQuery<PrepareSolverPairsJob, CollisionPair>(in _pairsQuery, ref prepareJob);

            // Phase 2: 迭代求解
            for (int iteration = 0; iteration < _config.SolverIterations; iteration++)
            {
                var solveJob = new SolvePairsJob
                {
                    Epsilon = _config.EpsilonFix64
                };
                World.InlineQuery<SolvePairsJob, CollisionPair>(in _pairsQuery, ref solveJob);
            }
        }

        private struct PrepareSolverPairsJob : IForEach<CollisionPair>
        {
            public World World;
            public Fix64 DefaultFriction;
            public Fix64 DefaultRestitution;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (pair.SensorOnly != 0) return;

                if (pair.ContactCount == 0)
                {
                    pair.AccumulatedNormalImpulse0 = Fix64.Zero;
                    pair.AccumulatedTangentImpulse0 = Fix64.Zero;
                    return;
                }

                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    pair.ContactCount = 0;
                    return;
                }

                Fix64 frictionA = pair.HasMaterialA != 0 ? pair.MaterialA.Friction : DefaultFriction;
                Fix64 frictionB = pair.HasMaterialB != 0 ? pair.MaterialB.Friction : DefaultFriction;
                Fix64 restitutionA = pair.HasMaterialA != 0 ? pair.MaterialA.Restitution : DefaultRestitution;
                Fix64 restitutionB = pair.HasMaterialB != 0 ? pair.MaterialB.Restitution : DefaultRestitution;

                pair.CombinedFriction = MaterialCombiner.CombineFriction(frictionA, frictionB);
                pair.CombinedRestitution = MaterialCombiner.CombineRestitution(restitutionA, restitutionB);

                ApplyWarmStart(ref pair);
            }
        }

        private struct SolvePairsJob : IForEach<CollisionPair>
        {
            public Fix64 Epsilon;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (pair.SensorOnly != 0) return;

                if (pair.ContactCount == 0) return;

                SolveContact0(ref pair, Epsilon);
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

        private static void SolveContact0(ref CollisionPair pair, Fix64 epsilon)
        {
            var relativeVelocity = pair.VelocityB.Linear - pair.VelocityA.Linear;
            Fix64 relativeNormalVelocity = Fix64Vec2.Dot(relativeVelocity, pair.Normal);

            if (relativeNormalVelocity >= Fix64.Zero) return;

            Fix64 effectiveMass = pair.MassA.InverseMass + pair.MassB.InverseMass;
            if (effectiveMass < epsilon) return;

            Fix64 normalImpulse = -(Fix64.OneValue + pair.CombinedRestitution) * relativeNormalVelocity / effectiveMass;
            normalImpulse = Fix64.Max(normalImpulse, Fix64.Zero);

            Fix64 oldNormal = pair.AccumulatedNormalImpulse0;
            Fix64 newNormal = Fix64.Max(oldNormal + normalImpulse, Fix64.Zero);
            Fix64 deltaNormal = newNormal - oldNormal;
            pair.AccumulatedNormalImpulse0 = newNormal;

            var tangent = new Fix64Vec2(-pair.Normal.Y, pair.Normal.X);
            Fix64 relativeTangentVelocity = Fix64Vec2.Dot(relativeVelocity, tangent);

            Fix64 tangentImpulse = Fix64.Zero;
            if (Fix64.Abs(relativeTangentVelocity) >= epsilon)
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
