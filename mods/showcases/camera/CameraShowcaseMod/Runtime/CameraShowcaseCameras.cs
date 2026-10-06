using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;

namespace CameraShowcaseMod.Runtime
{
    /// <summary>Camera request helpers shared by the runtime, the panel, and the F4 poll system.</summary>
    internal static class CameraShowcaseCameras
    {
        public static void SwitchToProfile(GameEngine engine, string cameraId)
        {
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = cameraId,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });
        }

        public static void RequestCollectionFollowCamera(GameEngine engine, string cameraId, Entity owner, float? blendDurationSeconds = 0f)
        {
            if (owner == Entity.Null ||
                !engine.World.IsAlive(owner) ||
                engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(cameraId, out var definition) ||
                definition == null)
            {
                return;
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = cameraId,
                BlendDurationSeconds = blendDurationSeconds,
                FollowTargetKindOverride = CameraFollowTargetKind.EntityCollectionPrimary,
                FollowCollectionOwnerOverride = owner,
                FollowCollectionKeyOverride = "collection.command.source",
                SnapToFollowTargetWhenAvailable = definition.SnapToFollowTargetWhenAvailable,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });
        }
    }
}
