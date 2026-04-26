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

        public ClearPresentationFlagsSystem(World world) : base(world) { }

        public override void Update(in float dt)
        {
            var commandBuffer = new CommandBuffer();
            var tagJob = new ClearTagJob { CommandBuffer = commandBuffer };
            World.InlineEntityQuery<ClearTagJob, GameplayTagEffectiveChangedBits>(in _tagQuery, ref tagJob);

            var attributeJob = new ClearAttributeJob { CommandBuffer = commandBuffer };
            World.InlineEntityQuery<ClearAttributeJob, GameplayAttributeChangedBits>(in _attributeQuery, ref attributeJob);
            commandBuffer.Playback(World, dispose: true);
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
