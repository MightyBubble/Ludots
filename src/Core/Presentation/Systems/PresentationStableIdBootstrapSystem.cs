using System;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Grants gameplay entities a presentation stable id so presenter lifecycle rules
    /// can observe entity spawn/destroy without depending on visual authoring side channels.
    /// </summary>
    public sealed class PresentationStableIdBootstrapSystem : BaseSystem<World, float>
    {
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly CommandBuffer _commandBuffer = new();
        private readonly QueryDescription _missingStableIdQuery = new QueryDescription()
            .WithAll<EntityTemplateKeyRef>()
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
                    _commandBuffer.Add(chunk.Entity(i), new PresentationStableId
                    {
                        Value = _stableIds.Allocate(),
                    });
                }
            }

            PlaybackStructuralChanges();
        }

        private void PlaybackStructuralChanges()
        {
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
