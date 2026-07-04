using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Runtime
{
    internal static class RtsShowcaseSelectionHelper
    {
        public static void EnsureSelectionViewBinding(GameEngine engine)
        {
            SelectionRuntime? selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
            Entity owner = ResolveSelectionOwner(engine);
            if (selection == null || !engine.World.IsAlive(owner))
            {
                return;
            }

            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
            engine.GlobalContext[CoreServiceKeys.EntityViewViewerEntity.Name] = owner;
            engine.GlobalContext[CoreServiceKeys.EntityViewKey.Name] = SelectionViewKeys.Primary;
        }

        public static bool TrySelectAndFocus(GameEngine engine, Entity target, bool snapCamera)
        {
            SelectionRuntime? selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
            if (selection == null || !engine.World.IsAlive(target))
            {
                return false;
            }

            Entity owner = ResolveSelectionOwner(engine);
            if (!engine.World.IsAlive(owner))
            {
                return false;
            }

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, next);
            EnsureSelectionViewBinding(engine);
            WriteCameraFocusRequests(engine, target, snapCamera);
            return true;
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

        private static Entity ResolveSelectionOwner(GameEngine engine)
        {
            Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (engine.World.IsAlive(owner))
            {
                return owner;
            }

            owner = engine.World.Create(new PlayerOwner { PlayerId = 1 });
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            engine.SetService(CoreServiceKeys.LocalPlayerId, 1);
            return owner;
        }
    }
}
