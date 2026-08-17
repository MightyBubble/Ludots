using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Runtime
{
    internal static class RtsShowcaseCommandSourceHelper
    {
        public static void EnsureCommandSourceBinding(GameEngine engine)
        {
            _ = RequireLocalCommandSourceOwner(engine);
        }

        public static bool TrySetCommandSourceAndFocus(GameEngine engine, Entity target, bool snapCamera)
        {
            if (!engine.World.IsAlive(target) ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return false;
            }

            Entity owner = RequireLocalCommandSourceOwner(engine);

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.CommandSource,
                owner,
                target,
                "RTS command source",
                "1 actor");
            collections.Replace(owner, descriptor, next, owner);
            EnsureCommandSourceBinding(engine);
            WriteCameraFocusRequests(engine, target, snapCamera);
            return true;
        }

        public static bool TryGetCommandSourcePrimary(GameEngine engine, out Entity primary)
        {
            Entity owner = RequireLocalCommandSourceOwner(engine);
            return Ludots.Core.Input.CommandSources.EntityCollectionContextRuntime.TryGetPrimary(
                engine.World,
                engine.GlobalContext,
                owner,
                EntityCollectionKeys.CommandSource,
                out primary);
        }

        public static int GetCommandSourceCount(GameEngine engine)
        {
            Entity owner = RequireLocalCommandSourceOwner(engine);
            return Ludots.Core.Input.CommandSources.EntityCollectionContextRuntime.GetCount(
                engine.GlobalContext,
                owner,
                EntityCollectionKeys.CommandSource);
        }

        public static void WriteCameraFocusRequests(GameEngine engine, Entity target, bool snapCamera)
        {
            if (!engine.World.IsAlive(target) ||
                !engine.World.TryGet(target, out WorldPositionCm worldPosition))
            {
                return;
            }

            MapConfig? mapConfig = engine.CurrentMapSession?.MapConfig;
            if (mapConfig == null)
            {
                return;
            }

            CameraConfig? cam = mapConfig.DefaultCamera;
            string virtualCameraId = string.IsNullOrWhiteSpace(cam?.VirtualCameraId)
                ? "Default"
                : cam.VirtualCameraId;

            engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
            {
                Id = virtualCameraId,
                BlendDurationSeconds = 0f,
                SnapToFollowTargetWhenAvailable = snapCamera,
                ResetRuntimeState = snapCamera
            };

            engine.GlobalContext[CoreServiceKeys.CameraPoseRequest.Name] = new CameraPoseRequest
            {
                VirtualCameraId = virtualCameraId,
                TargetCm = worldPosition.Value.ToVector2(),
                Yaw = cam?.Yaw,
                Pitch = cam?.Pitch,
                DistanceCm = ResolveFocusDistance(cam?.DistanceCm),
                FovYDeg = cam?.FovYDeg
            };
        }

        private static float? ResolveFocusDistance(float? distanceCm)
        {
            if (!distanceCm.HasValue || distanceCm.Value <= 0f)
            {
                return distanceCm;
            }

            return MathF.Max(7000f, distanceCm.Value * 0.72f);
        }

        private static Entity RequireLocalCommandSourceOwner(GameEngine engine)
        {
            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    "RTS showcase requires a live sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
            }

            return owner;
        }
    }
}
