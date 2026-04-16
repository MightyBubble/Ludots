using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Ensures projectile entities expose the minimal presentation contract
    /// needed by performer observers.
    /// </summary>
    public sealed class ProjectilePresentationBootstrapSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<ProjectileState, WorldPositionCm>()
            .WithNone<ProjectilePresentationBootstrapState>();

        private readonly PresentationStableIdAllocator _stableIds;

        public ProjectilePresentationBootstrapSystem(
            World world,
            PresentationStableIdAllocator stableIds)
            : base(world)
        {
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
        }

        public override void Update(in float dt)
        {
            var query = World.Query(in Query);
            foreach (var chunk in query)
            {
                var projectiles = chunk.GetArray<ProjectileState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    World.Add(entity, new ProjectilePresentationBootstrapState());

                    EnsurePresentationContract(entity);

                }
            }
        }

        private void EnsurePresentationContract(Entity entity)
        {
            if (!World.Has<VisualTransform>(entity))
            {
                World.Add(entity, VisualTransform.Default);
            }

            if (!World.Has<CullState>(entity))
            {
                World.Add(entity, new CullState { IsVisible = true, LOD = LODLevel.High });
            }

            if (!World.Has<PresentationStableId>(entity))
            {
                World.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
            }
        }
    }
}
