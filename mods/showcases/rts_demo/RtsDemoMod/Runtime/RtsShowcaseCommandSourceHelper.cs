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
            if (TryResolveLocalCommandSourceOwner(engine, out Entity owner) &&
                !engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    "RTS showcase requires a live sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
            }
        }

        public static bool TrySetCommandSourceAndFocus(GameEngine engine, Entity target, bool snapCamera)
        {
            if (!engine.World.IsAlive(target) ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return false;
            }

            if (!TryResolveLocalCommandSourceOwner(engine, out Entity owner))
            {
                return false;
            }

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
            primary = Entity.Null;
            if (!TryResolveLocalCommandSourceOwner(engine, out Entity owner))
            {
                return false;
            }

            return Ludots.Core.Input.CommandSources.EntityCollectionContextRuntime.TryGetPrimary(
                engine.World,
                engine.GlobalContext,
                owner,
                EntityCollectionKeys.CommandSource,
                out primary);
        }

        public static int GetCommandSourceCount(GameEngine engine)
        {
            if (!TryResolveLocalCommandSourceOwner(engine, out Entity owner))
            {
                return 0;
            }

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
            RtsCommandSourceUiMapConfig uiConfig = RtsCommandSourceUiMapConfig.Resolve(mapConfig);
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

            Vector2 focusTarget = worldPosition.Value.ToVector2();
            if (uiConfig.CameraFocusTowardDefaultTargetCm > 0f)
            {
                if (cam?.TargetXCm is not float defaultTargetXCm ||
                    cam.TargetYCm is not float defaultTargetYCm)
                {
                    throw new InvalidOperationException(
                        $"RTS map '{mapConfig.Id}' requires a complete DefaultCamera target when '{RtsCommandSourceUiMapConfig.MetadataKey}.cameraFocusTowardDefaultTargetCm' is positive.");
                }

                Vector2 direction = new Vector2(defaultTargetXCm, defaultTargetYCm) - focusTarget;
                if (direction.LengthSquared() <= float.Epsilon)
                {
                    throw new InvalidOperationException(
                        $"RTS map '{mapConfig.Id}' cannot offset camera focus toward its default target because the command source already occupies that target.");
                }

                focusTarget += Vector2.Normalize(direction) * uiConfig.CameraFocusTowardDefaultTargetCm;
            }

            engine.GlobalContext[CoreServiceKeys.CameraPoseRequest.Name] = new CameraPoseRequest
            {
                VirtualCameraId = virtualCameraId,
                TargetCm = focusTarget,
                Yaw = cam?.Yaw,
                Pitch = cam?.Pitch,
                DistanceCm = uiConfig.CameraFocusDistanceCm ?? cam?.DistanceCm,
                FovYDeg = uiConfig.CameraFocusFovYDeg ?? cam?.FovYDeg
            };
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out owner))
            {
                owner = Entity.Null;
                return false;
            }

            if (!engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    "RTS showcase requires a live sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
            }

            return true;
        }
    }
}
