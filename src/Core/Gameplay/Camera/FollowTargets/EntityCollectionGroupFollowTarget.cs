using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class EntityCollectionGroupFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly EntityCollectionStore _collections;
        private readonly Entity _owner;
        private readonly string _collectionKey;

        public EntityCollectionGroupFollowTarget(
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
            return TryGetCollectionCentroid(out positionCm);
        }

        private bool TryGetCollectionCentroid(out Vector2 positionCm)
        {
            positionCm = default;
            if (_owner == Entity.Null ||
                string.IsNullOrWhiteSpace(_collectionKey) ||
                !_collections.TryGet(_owner, _collectionKey, out EntityCollectionHandle handle) ||
                !_collections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            Vector2 weightedSum = Vector2.Zero;
            float totalWeight = 0f;

            for (int i = 0; i < view.Count; i++)
            {
                if (!_collections.TryGetEntityAt(handle, i, out Entity entity) ||
                    !_world.IsAlive(entity) ||
                    !_world.Has<WorldPositionCm>(entity))
                {
                    continue;
                }

                Vector2 entityPosition = _world.Get<WorldPositionCm>(entity).Value.ToVector2();
                float weight = ResolveWeight(entity);
                weightedSum += entityPosition * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            positionCm = weightedSum / totalWeight;
            return true;
        }

        private float ResolveWeight(Entity entity)
        {
            if (_world.Has<CameraFollowWeight>(entity))
            {
                float configured = _world.Get<CameraFollowWeight>(entity).Value;
                if (configured > 0f)
                {
                    return configured;
                }
            }

            return 1f;
        }
    }
}
