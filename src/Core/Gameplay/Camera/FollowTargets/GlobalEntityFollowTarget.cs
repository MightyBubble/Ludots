using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class GlobalEntityFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly string _globalKey;

        public GlobalEntityFollowTarget(World world, Dictionary<string, object> globals, string globalKey)
        {
            _world = world;
            _globals = globals;
            _globalKey = globalKey;
        }

        public bool TryGetTransform(out CameraTargetTransformSnapshot transform)
        {
            transform = default;
            if (!_globals.TryGetValue(_globalKey, out var value) || value is not Entity entity)
            {
                return false;
            }

            if (!_world.IsAlive(entity) || !_world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            var position = _world.Get<WorldPositionCm>(entity).Value;
            bool hasFacing = _world.TryGet(entity, out FacingDirection facing);
            transform = new CameraTargetTransformSnapshot(
                position.ToVector2(),
                hasFacingYawRad: hasFacing,
                facingYawRad: hasFacing ? facing.AngleRad : 0f);
            return true;
        }
    }
}
