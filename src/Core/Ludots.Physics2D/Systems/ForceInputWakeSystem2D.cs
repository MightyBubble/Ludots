using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class ForceInputWakeSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _sleepingForceInputQuery = new QueryDescription()
            .WithAll<ForceInput2D, SleepingTag, Mass2D>();

        private readonly CommandBuffer _commandBuffer = new();

        public ForceInputWakeSystem2D(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
            foreach (ref var chunk in World.Query(in _sleepingForceInputQuery))
            {
                var job = new WakeJob { CommandBuffer = _commandBuffer };
                job.Execute(ref chunk);
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private struct WakeJob
        {
            public CommandBuffer CommandBuffer;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                var forces = chunk.GetSpan<ForceInput2D>();
                var masses = chunk.GetSpan<Mass2D>();
                bool hasMotion = chunk.Has<Motion>();
                Span<Motion> motions = hasMotion ? chunk.GetSpan<Motion>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    if (masses[index].IsStatic || forces[index].Force == Fix64Vec2.Zero)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    CommandBuffer.Remove<SleepingTag>(in entity);

                    if (hasMotion)
                    {
                        motions[index].SleepTimer = 0;
                    }
                }
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
