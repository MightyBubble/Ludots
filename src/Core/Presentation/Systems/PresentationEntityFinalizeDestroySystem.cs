using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Finalizes entity destruction after presentation observers have consumed
    /// the destroy fact for one frame.
    /// </summary>
    public sealed class PresentationEntityFinalizeDestroySystem : BaseSystem<World, float>
    {
        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<PresentationDestroyPending, PresentationDestroyEventPublished>();

        public PresentationEntityFinalizeDestroySystem(World world)
            : base(world)
        {
        }

        public override void Update(in float dt)
        {
            var query = World.Query(in _query);
            foreach (var chunk in query)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    if (World.IsAlive(entity))
                    {
                        World.Destroy(entity);
                    }
                }
            }
        }
    }
}
