using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 运动状态更新系统 — 全定点数域，计算速度并管理休眠计时器。
    /// </summary>
    public sealed class UpdateMotionSystem : BaseSystem<World, float>
    {
        private static readonly Fix64 LinearMotionThreshold = Fix64.FromFloat(0.01f);
        private static readonly Fix64 AngularMotionThreshold = Fix64.FromFloat(0.01f);

        private static readonly QueryDescription _motionQuery =
            new QueryDescription().WithAll<Motion, Velocity2D, Mass2D>();

        private static readonly QueryDescription _initializationQuery =
            new QueryDescription().WithAll<Velocity2D, Mass2D>().WithNone<Motion>();

        private readonly CommandBuffer _commandBuffer = new();

        public UpdateMotionSystem(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
            InitializeMissingMotion();

            var job = new MotionUpdateJob();
            World.InlineQuery<MotionUpdateJob, Motion, Velocity2D, Mass2D>(in _motionQuery, ref job);
        }

        private void InitializeMissingMotion()
        {
            World.Query(in _initializationQuery, (Entity entity, ref Velocity2D velocity, ref Mass2D mass) =>
            {
                if (mass.IsStatic) return;
                _commandBuffer.Add(entity, new Motion
                {
                    LinearSpeed = velocity.Linear.Length(),
                    AngularSpeed = Fix64.Abs(velocity.Angular),
                    SleepTimer = 0
                });
            });

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private struct MotionUpdateJob : IForEach<Motion, Velocity2D, Mass2D>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Motion motion, ref Velocity2D velocity, ref Mass2D mass)
            {
                if (mass.IsStatic) return;

                motion.LinearSpeed = velocity.Linear.Length();
                motion.AngularSpeed = Fix64.Abs(velocity.Angular);

                bool isAlmostStationary = motion.LinearSpeed < LinearMotionThreshold &&
                                          motion.AngularSpeed < AngularMotionThreshold;

                if (isAlmostStationary)
                {
                    motion.SleepTimer++;
                    if (motion.SleepTimer < 0) motion.SleepTimer = int.MaxValue;
                }
                else
                {
                    motion.SleepTimer = 0;
                }
            }
        }
    }
}
