using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class ViewedSelectionPrimaryFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime? _selection;

        public ViewedSelectionPrimaryFollowTarget(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var selectionObj) &&
                         selectionObj is SelectionRuntime selection
                ? selection
                : null;
        }

        public bool TryGetTransform(out CameraTargetTransformSnapshot transform)
        {
            transform = default;
            if (_selection == null ||
                !SelectionViewRuntime.TryGetViewedPrimary(_world, _globals, _selection, out Entity entity) ||
                !_world.IsAlive(entity) ||
                !_world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            bool hasFacing = _world.TryGet(entity, out FacingDirection facing);
            transform = new CameraTargetTransformSnapshot(
                _world.Get<WorldPositionCm>(entity).Value.ToVector2(),
                hasFacingYawRad: hasFacing,
                facingYawRad: hasFacing ? facing.AngleRad : 0f);
            return true;
        }
    }
}
