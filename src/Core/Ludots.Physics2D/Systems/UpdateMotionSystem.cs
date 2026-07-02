using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 运动状态更新系统 — 全定点数域，计算速度并管理休眠计时器。
    /// </summary>
    public sealed class UpdateMotionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _motionQuery =
            new QueryDescription().WithAll<Motion, Velocity2D, Mass2D>();

        private static readonly QueryDescription _initializationQuery =
            new QueryDescription().WithAll<Velocity2D, Mass2D>().WithNone<Motion>();

        private readonly CommandBuffer _commandBuffer = new();
        private readonly Physics2DSolverConfig _config;

        public UpdateMotionSystem(World world, Physics2DSolverConfig config) : base(world)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override void Update(in float deltaTime)
        {
            InitializeMissingMotion();

            var job = new MotionUpdateJob
            {
                LinearMotionThreshold = _config.LinearMotionThresholdFix64,
                AngularMotionThreshold = _config.AngularMotionThresholdFix64
            };
            World.InlineQuery<MotionUpdateJob, Motion, Velocity2D, Mass2D>(in _motionQuery, ref job);
        }

        private void InitializeMissingMotion()
        {
            var job = new InitializeMotionJob { CommandBuffer = _commandBuffer };
            World.InlineEntityQuery<InitializeMotionJob, Velocity2D, Mass2D>(in _initializationQuery, ref job);

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private struct InitializeMotionJob : IForEachWithEntity<Velocity2D, Mass2D>
        {
            public CommandBuffer CommandBuffer;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Velocity2D velocity, ref Mass2D mass)
            {
                if (mass.IsStatic) return;
                CommandBuffer.Add(entity, new Motion
                {
                    LinearSpeed = velocity.Linear.Length(),
                    AngularSpeed = Fix64.Abs(velocity.Angular),
                    SleepTimer = 0
                });
            }
        }

        private struct MotionUpdateJob : IForEach<Motion, Velocity2D, Mass2D>
        {
            public Fix64 LinearMotionThreshold;
            public Fix64 AngularMotionThreshold;

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
