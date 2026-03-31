using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Networking.Components;

namespace Ludots.Core.Networking.Systems
{
    public sealed class GameplayReplicationBootstrapSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription MissingReplicationIdQuery = new QueryDescription()
            .WithAll<WorldPositionCm>()
            .WithNone<GameplayReplicationEntityId>();

        private readonly GameplayReplicationEntityIdAllocator _allocator;

        public GameplayReplicationBootstrapSystem(World world, GameplayReplicationEntityIdAllocator allocator)
            : base(world)
        {
            _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        }

        public override void Update(in float dt)
        {
            var query = World.Query(in MissingReplicationIdQuery);
            foreach (var chunk in query)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    World.Add(entity, new GameplayReplicationEntityId
                    {
                        Value = _allocator.Allocate(),
                    });
                }
            }
        }
    }
}
