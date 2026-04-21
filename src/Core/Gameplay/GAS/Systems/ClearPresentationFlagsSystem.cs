using Arch.Core;
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
            var tagJob = new ClearTagJob();
            World.InlineEntityQuery<ClearTagJob, GameplayTagEffectiveChangedBits>(in _tagQuery, ref tagJob);

            var attributeJob = new ClearAttributeJob();
            World.InlineEntityQuery<ClearAttributeJob, GameplayAttributeChangedBits>(in _attributeQuery, ref attributeJob);
        }

        private struct ClearTagJob : IForEachWithEntity<GameplayTagEffectiveChangedBits>
        {
            public void Update(Entity entity, ref GameplayTagEffectiveChangedBits bits)
            {
                bits.Clear();
            }
        }

        private struct ClearAttributeJob : IForEachWithEntity<GameplayAttributeChangedBits>
        {
            public void Update(Entity entity, ref GameplayAttributeChangedBits bits)
            {
                bits.Clear();
            }
        }
    }
}
