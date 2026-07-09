using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class EntityCollectionPrimaryFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly EntityCollectionStore _collections;
        private readonly Entity _owner;
        private readonly string _collectionKey;

        public EntityCollectionPrimaryFollowTarget(
            World world,
            EntityCollectionStore collections,
            Entity owner,
            string collectionKey)
        {
            _world = world;
            _collections = collections;
            _owner = owner;
            _collectionKey = collectionKey;
        }

        public bool TryGetPosition(out Vector2 positionCm)
        {
            positionCm = default;
            if (_owner == Entity.Null ||
                string.IsNullOrWhiteSpace(_collectionKey) ||
                !_collections.TryGet(_owner, _collectionKey, out EntityCollectionHandle handle) ||
                !_collections.TryGetEntityAt(handle, 0, out Entity entity) ||
                !_world.IsAlive(entity) ||
                !_world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            positionCm = _world.Get<WorldPositionCm>(entity).Value.ToVector2();
            return true;
        }

        public bool TryGetTransform(out CameraTargetTransformSnapshot transform)
        {
            if (TryGetPosition(out Vector2 positionCm))
            {
                transform = new CameraTargetTransformSnapshot(positionCm);
                return true;
            }

            transform = default;
            return false;
        }
    }
}
