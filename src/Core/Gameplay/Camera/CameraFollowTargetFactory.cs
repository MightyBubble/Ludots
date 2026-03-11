using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Camera
{
    public static class CameraFollowTargetFactory
    {
        public static ICameraFollowTarget? Build(
            World world,
            Dictionary<string, object> globals,
            CameraFollowTargetKind kind)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(globals);

            return kind switch
            {
                CameraFollowTargetKind.None => null,
                CameraFollowTargetKind.LocalPlayer => new GlobalEntityFollowTarget(world, globals, CoreServiceKeys.LocalPlayerEntity.Name),
                CameraFollowTargetKind.SelectedEntity => new GlobalEntityFollowTarget(world, globals, CoreServiceKeys.SelectedEntity.Name),
                _ => throw new InvalidOperationException($"Unsupported camera follow target kind: {kind}")
            };
        }
    }
}
