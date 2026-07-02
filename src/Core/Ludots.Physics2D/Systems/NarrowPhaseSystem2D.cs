using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Collision;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 窄相碰撞检测系统 — 全定点数域，确保跨平台确定性。
    /// </summary>
    public sealed class NarrowPhaseSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _pairsQuery;
        private readonly ShapeDataStorage2D _shapeStorage;

        public NarrowPhaseSystem2D(World world, ShapeDataStorage2D shapeStorage) : base(world)
        {
            _pairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            _shapeStorage = shapeStorage ?? throw new ArgumentNullException(nameof(shapeStorage));
        }

        public override void Update(in float deltaTime)
        {
            var job = new NarrowPhaseJob
            {
                World = World,
                ShapeStorage = _shapeStorage
            };
            World.InlineQuery<NarrowPhaseJob, CollisionPair>(in _pairsQuery, ref job);
        }

        private struct NarrowPhaseJob : IForEach<CollisionPair>
        {
            public World World;
            public ShapeDataStorage2D ShapeStorage;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    pair.ContactCount = 0;
                    pair.Penetration = Fix64.Zero;
                    return;
                }

                bool hasCollision = CollisionAlgorithms2D.Detect(
                    ShapeStorage,
                    pair.PositionA.Value, pair.RotationA, pair.ColliderA,
                    pair.PositionB.Value, pair.RotationB, pair.ColliderB,
                    out Fix64Vec2 normal,
                    out Fix64 penetration);

                if (hasCollision)
                {
                    pair.Normal = normal;
                    pair.Penetration = penetration;
                    pair.ContactCount = 1;
                }
                else
                {
                    pair.ContactCount = 0;
                    pair.Penetration = Fix64.Zero;
                    pair.AccumulatedNormalImpulse0 = Fix64.Zero;
                    pair.AccumulatedTangentImpulse0 = Fix64.Zero;
                }
            }
        }
    }
}
