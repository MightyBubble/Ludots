using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ClearPresentationFlagsSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _q = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits>();

        public ClearPresentationFlagsSystem(World world) : base(world) { }

        public override void Update(in float dt)
        {
            var job = new Job();
            World.InlineEntityQuery<Job, GameplayTagEffectiveChangedBits>(in _q, ref job);
        }

        private struct Job : IForEachWithEntity<GameplayTagEffectiveChangedBits>
        {
            public void Update(Entity entity, ref GameplayTagEffectiveChangedBits bits)
            {
                bits.Clear();
            }
        }
    }
}
