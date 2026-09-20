using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    /// <summary>Follow the sole client-local possessed participant rep (Epic #896).</summary>
    public sealed class SolePossessedRepFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;

        public SolePossessedRepFollowTarget(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public bool TryGetTransform(out CameraTargetTransformSnapshot transform)
        {
            transform = default;
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity entity) ||
                !_world.IsAlive(entity) ||
                !_world.Has<WorldPositionCm>(entity))
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
