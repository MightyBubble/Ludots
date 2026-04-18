using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Grants gameplay entities a presentation stable id so performer lifecycle rules
    /// can observe entity spawn/destroy without depending on legacy visual authoring.
    /// </summary>
    public sealed class PresentationStableIdBootstrapSystem : BaseSystem<World, float>
    {
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly QueryDescription _missingStableIdQuery = new QueryDescription()
            .WithAll<EntityTemplateKeyCm>()
            .WithNone<PresentationStableId>();

        public PresentationStableIdBootstrapSystem(World world, PresentationStableIdAllocator stableIds)
            : base(world)
        {
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
        }

        public override void Update(in float dt)
        {
            var query = World.Query(in _missingStableIdQuery);
            foreach (var chunk in query)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    World.Add(chunk.Entity(i), new PresentationStableId
                    {
                        Value = _stableIds.Allocate(),
                    });
                }
            }
        }
    }
}
