using Arch.Core;
using Arch.Buffer;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ClearPresentationFlagsSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _tagQuery = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits>();
        private static readonly QueryDescription _attributeQuery = new QueryDescription()
            .WithAll<GameplayAttributeChangedBits>();
        private readonly CommandBuffer _commandBuffer = new();

        public ClearPresentationFlagsSystem(World world) : base(world) { }

        public override void Update(in float dt)
        {
            Clear(World, _commandBuffer);
        }

        internal static void Clear(World world, CommandBuffer commandBuffer)
        {
            var tagJob = new ClearTagJob { CommandBuffer = commandBuffer };
            world.InlineEntityQuery<ClearTagJob, GameplayTagEffectiveChangedBits>(in _tagQuery, ref tagJob);

            var attributeJob = new ClearAttributeJob { CommandBuffer = commandBuffer };
            world.InlineEntityQuery<ClearAttributeJob, GameplayAttributeChangedBits>(in _attributeQuery, ref attributeJob);
            if (commandBuffer.Size > 0)
            {
                commandBuffer.Playback(world);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        private struct ClearTagJob : IForEachWithEntity<GameplayTagEffectiveChangedBits>
        {
            public CommandBuffer CommandBuffer;

            public void Update(Entity entity, ref GameplayTagEffectiveChangedBits bits)
            {
                bits.Clear();
                CommandBuffer.Remove<GameplayTagEffectiveChangedBits>(entity);
            }
        }

        private struct ClearAttributeJob : IForEachWithEntity<GameplayAttributeChangedBits>
        {
            public CommandBuffer CommandBuffer;

            public void Update(Entity entity, ref GameplayAttributeChangedBits bits)
            {
                bits.Clear();
                CommandBuffer.Remove<GameplayAttributeChangedBits>(entity);
            }
        }
    }
}
