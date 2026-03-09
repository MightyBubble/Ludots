using System.Collections.Generic;
using Ludots.Core.Gameplay.Camera.Behaviors;

namespace Ludots.Core.Gameplay.Camera
{
    internal static class CameraControllerFactory
    {
        public static CompositeCameraController FromPreset(CameraPreset preset, CameraBehaviorContext ctx)
        {
            var behaviors = new List<ICameraBehavior>();

            if (preset.EnableZoom)
            {
                behaviors.Add(new ZoomBehavior(
                    preset.ZoomActionId, preset.ZoomCmPerWheel,
                    preset.MinDistanceCm, preset.MaxDistanceCm));
            }

            switch (preset.PanMode)
            {
                case CameraPanMode.Keyboard:
                    behaviors.Add(new KeyboardPanBehavior(preset.MoveActionId, preset.PanCmPerSecond));
                    break;
                case CameraPanMode.EdgePan:
                    behaviors.Add(new EdgePanBehavior(preset.PointerPosActionId, preset.EdgePanMarginPx, preset.EdgePanSpeedCmPerSec));
                    break;
                case CameraPanMode.KeyboardAndEdge:
                    behaviors.Add(new KeyboardPanBehavior(preset.MoveActionId, preset.PanCmPerSecond));
                    behaviors.Add(new EdgePanBehavior(preset.PointerPosActionId, preset.EdgePanMarginPx, preset.EdgePanSpeedCmPerSec));
                    break;
            }

            if (preset.EnableGrabDrag)
            {
                behaviors.Add(new GrabDragPanBehavior(preset.GrabDragHoldActionId, preset.PointerPosActionId));
            }

            switch (preset.RotateMode)
            {
                case CameraRotateMode.DragRotate:
                    behaviors.Add(new DragRotateBehavior(
                        preset.RotateHoldActionId, preset.PointerPosActionId,
                        preset.RotateDegPerPixel, preset.MinPitchDeg, preset.MaxPitchDeg));
                    break;
                case CameraRotateMode.KeyRotate:
                    behaviors.Add(new KeyRotateBehavior(preset.RotateLeftActionId, preset.RotateRightActionId, preset.RotateDegPerSecond));
                    break;
                case CameraRotateMode.Both:
                    behaviors.Add(new DragRotateBehavior(
                        preset.RotateHoldActionId, preset.PointerPosActionId,
                        preset.RotateDegPerPixel, preset.MinPitchDeg, preset.MaxPitchDeg));
                    behaviors.Add(new KeyRotateBehavior(preset.RotateLeftActionId, preset.RotateRightActionId, preset.RotateDegPerSecond));
                    break;
            }

            return new CompositeCameraController(behaviors.ToArray(), ctx);
        }
    }
}
