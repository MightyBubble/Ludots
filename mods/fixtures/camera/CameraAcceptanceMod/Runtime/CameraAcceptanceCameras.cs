using System;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Runtime
{
    /// <summary>
    /// Acceptance camera switching shared by the mode-key system and the panel. Collection-based
    /// follow definitions (EntityCollectionPrimary / Group) require an explicit collection owner:
    /// the sole possessed rep, resolved here — the same contract the fixture's follow maps rely on.
    /// </summary>
    internal static class CameraAcceptanceCameras
    {
        public static void SwitchToProfile(GameEngine engine, string cameraId)
        {
            if (engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(cameraId, out var definition) ||
                definition == null)
            {
                throw new InvalidOperationException(
                    $"Camera acceptance requires virtual camera '{cameraId}'.");
            }

            Entity collectionOwner = Entity.Null;
            if (CameraFollowTargetFactory.RequiresEntityCollection(definition.FollowTargetKind))
            {
                if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity owner) ||
                    owner == Entity.Null ||
                    !engine.World.IsAlive(owner))
                {
                    throw new InvalidOperationException(
                        $"Camera acceptance follow camera '{cameraId}' requires a live sole ClientLocalSeat possession.");
                }

                collectionOwner = owner;
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = cameraId,
                FollowCollectionOwnerOverride = collectionOwner,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });
        }
    }
}
