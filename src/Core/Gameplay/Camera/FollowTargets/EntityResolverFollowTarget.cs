using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    /// <summary>
    /// Generic follow target that resolves its backing entity on demand.
    /// Composition layers decide how to source the entity without teaching camera core about gameplay semantics.
    /// </summary>
    public sealed class EntityResolverFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Func<Entity> _resolveEntity;

        public EntityResolverFollowTarget(World world, Func<Entity> resolveEntity)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _resolveEntity = resolveEntity ?? throw new ArgumentNullException(nameof(resolveEntity));
        }

        public bool TryGetPosition(out Vector2 positionCm)
        {
            positionCm = default;
            var entity = _resolveEntity();
            if (!_world.IsAlive(entity) || !_world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            positionCm = _world.Get<WorldPositionCm>(entity).Value.ToVector2();
            return true;
        }
    }
}
