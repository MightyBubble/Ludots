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

        public bool TryGetPosition(out Vector2 positionCm)
        {
            positionCm = default;
            if (!_globals.TryGetValue(_globalKey, out var value) || value is not Entity entity)
            {
                return false;
            }

            if (!_world.IsAlive(entity) || !_world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            var position = _world.Get<WorldPositionCm>(entity).Value;
            positionCm = position.ToVector2();
            return true;
        }
    }
}
