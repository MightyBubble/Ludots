using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation;
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
        private readonly List<Entity> _finalizeScratch = new();

        public PresentationEntityFinalizeDestroySystem(World world)
            : base(world)
        {
            PresentationLifecycleConsumption.InstallConsumer(world);
        }

        public override void Update(in float dt)
        {
            _finalizeScratch.Clear();
            var query = World.Query(in _query);
            foreach (var chunk in query)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    _finalizeScratch.Add(chunk.Entity(i));
                }
            }

            for (int i = 0; i < _finalizeScratch.Count; i++)
            {
                FinalizePublished(World, _finalizeScratch[i]);
            }
        }

        public static void FinalizePublished(World world, Entity entity)
        {
            if (world.IsAlive(entity)
                && world.Has<PresentationDestroyPending>(entity)
                && world.Has<PresentationDestroyEventPublished>(entity))
            {
                world.Destroy(entity);
            }
        }

        public static void FinalizeWithoutConsumer(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<PresentationDestroyPending>(entity))
            {
                return;
            }

            if (!world.Has<PresentationDestroyEventPublished>(entity))
            {
                world.Add(entity, new PresentationDestroyEventPublished());
            }

            FinalizePublished(world, entity);
        }
    }
}
