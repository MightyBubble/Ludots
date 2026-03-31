using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GameplayControlStateBootstrapSystem : BaseSystem<World, float>
    {
        private readonly CommandBuffer _commandBuffer = new();
        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<AttributeBuffer>()
            .WithNone<GameplayControlState>();

        public GameplayControlStateBootstrapSystem(World world)
            : base(world)
        {
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in _query))
            {
                ref var entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    _commandBuffer.Add(entity, GameplayControlState.CreateDefault());
                }
            }

            _commandBuffer.Playback(World, dispose: true);
        }
    }
}
