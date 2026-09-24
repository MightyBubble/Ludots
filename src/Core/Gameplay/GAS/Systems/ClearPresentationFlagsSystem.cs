using Arch.Core;
using Arch.Buffer;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ClearPresentationFlagsSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _tagQuery = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits>();
        private readonly GameplayAttributeChangedChannel _attributeChanges;
        private readonly CommandBuffer _commandBuffer = new();

        public ClearPresentationFlagsSystem(World world, GameplayAttributeChangedChannel attributeChanges) : base(world)
        {
            _attributeChanges = attributeChanges ?? throw new System.ArgumentNullException(nameof(attributeChanges));
        }

        public override void Update(in float dt)
        {
            var tagJob = new ClearTagJob { CommandBuffer = _commandBuffer };
            World.InlineEntityQuery<ClearTagJob, GameplayTagEffectiveChangedBits>(in _tagQuery, ref tagJob);
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }

            _attributeChanges.Clear();
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
    }
}
