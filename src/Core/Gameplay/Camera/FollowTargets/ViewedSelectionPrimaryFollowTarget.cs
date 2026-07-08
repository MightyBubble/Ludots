using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Input.CommandSources;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class ViewedSelectionPrimaryFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;

        public ViewedSelectionPrimaryFollowTarget(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public bool TryGetPosition(out Vector2 positionCm)
        {
            positionCm = default;
            if (!EntityCollectionContextRuntime.TryGetCurrentPrimary(_world, _globals, out Entity entity) ||
                !_world.IsAlive(entity) ||
                !_world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            positionCm = _world.Get<WorldPositionCm>(entity).Value.ToVector2();
            return true;
        }
    }
}
