using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Networking.Components;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Networking.Systems
{
    public sealed class GameplayReplicationEmitSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription ReplicationQuery = new QueryDescription()
            .WithAll<WorldPositionCm>();

        private readonly GameplayReplicationEntityIdAllocator _allocator;
        private readonly GameplayReplicationSnapshotBuffer _buffer;
        private readonly GameSession _gameSession;

        public GameplayReplicationEmitSystem(
            World world,
            GameplayReplicationEntityIdAllocator allocator,
            GameplayReplicationSnapshotBuffer buffer,
            GameSession gameSession)
            : base(world)
        {
            _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _gameSession = gameSession ?? throw new ArgumentNullException(nameof(gameSession));
        }

        public override void Update(in float dt)
        {
            _buffer.BeginRebuild(_gameSession.CurrentTick);

            var query = World.Query(in ReplicationQuery);
            foreach (var chunk in query)
            {
                var positions = chunk.GetArray<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    int replicationEntityId = EnsureReplicationEntityId(entity);
                    if (replicationEntityId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Gameplay replication snapshot requires a positive GameplayReplicationEntityId for entity #{entity.Id}:{entity.WorldId}.");
                    }

                    var position = positions[i].Value;
                    var item = new GameplayReplicationSnapshotItem
                    {
                        ReplicationEntityId = replicationEntityId,
                        PositionXRaw = position.X.RawValue,
                        PositionYRaw = position.Y.RawValue,
                    };

                    GameplayReplicationSnapshotFlags flags = GameplayReplicationSnapshotFlags.None;
                    if (World.Has<FacingDirection>(entity))
                    {
                        item.FacingAngleRad = World.Get<FacingDirection>(entity).AngleRad;
                        flags |= GameplayReplicationSnapshotFlags.HasFacing;
                    }

                    if (World.Has<Team>(entity))
                    {
                        item.TeamId = World.Get<Team>(entity).Id;
                        flags |= GameplayReplicationSnapshotFlags.HasTeam;
                    }

                    if (World.Has<PlayerOwner>(entity))
                    {
                        item.PlayerId = World.Get<PlayerOwner>(entity).PlayerId;
                        flags |= GameplayReplicationSnapshotFlags.HasPlayerOwner;
                    }

                    if (World.Has<PresentationStableId>(entity))
                    {
                        item.PresentationStableId = World.Get<PresentationStableId>(entity).Value;
                        flags |= GameplayReplicationSnapshotFlags.HasPresentationStableId;
                    }

                    item.Flags = flags;
                    _buffer.TryAdd(item);
                }
            }
        }

        private int EnsureReplicationEntityId(Entity entity)
        {
            if (World.Has<GameplayReplicationEntityId>(entity))
            {
                return World.Get<GameplayReplicationEntityId>(entity).Value;
            }

            int value = _allocator.Allocate();
            World.Add(entity, new GameplayReplicationEntityId
            {
                Value = value,
            });
            return value;
        }
    }
}
