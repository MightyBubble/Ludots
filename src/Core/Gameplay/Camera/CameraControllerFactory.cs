using System.Collections.Generic;
using Ludots.Core.Gameplay.Camera.Behaviors;

namespace Ludots.Core.Gameplay.Camera
{
    internal static class CameraControllerFactory
    {
        public static CompositeCameraController FromDefinition(VirtualCameraDefinition definition, CameraBehaviorContext ctx)
        {
            var behaviors = new List<ICameraBehavior>();

            if (definition.EnableZoom)
            {
                behaviors.Add(new ZoomBehavior(
                    definition.ZoomActionId, definition.ZoomCmPerWheel,
                    definition.MinDistanceCm, definition.MaxDistanceCm));
            }

            switch (definition.PanMode)
            {
                case CameraPanMode.Keyboard:
                    behaviors.Add(new KeyboardPanBehavior(definition.MoveActionId, definition.PanCmPerSecond));
                    break;
                case CameraPanMode.EdgePan:
                    behaviors.Add(new EdgePanBehavior(
                        definition.PointerPosActionId,
                        definition.EdgePanMarginPx,
                        definition.EdgePanSpeedCmPerSec,
                        definition.EdgePanRequiresPointerInsideViewport));
                    break;
                case CameraPanMode.KeyboardAndEdge:
                    behaviors.Add(new KeyboardPanBehavior(definition.MoveActionId, definition.PanCmPerSecond));
                    behaviors.Add(new EdgePanBehavior(
                        definition.PointerPosActionId,
                        definition.EdgePanMarginPx,
                        definition.EdgePanSpeedCmPerSec,
                        definition.EdgePanRequiresPointerInsideViewport));
                    break;
            }

            if (definition.EnableGrabDrag)
            {
                behaviors.Add(new GrabDragPanBehavior(definition.GrabDragHoldActionId, definition.PointerDeltaActionId));
            }

            switch (definition.RotateMode)
            {
                case CameraRotateMode.DragRotate:
                    behaviors.Add(new DragRotateBehavior(
                        definition.RotateHoldActionId, definition.LookActionId,
                        definition.RotateDegPerPixel, definition.MinPitchDeg, definition.MaxPitchDeg));
                    break;
                case CameraRotateMode.KeyRotate:
                    behaviors.Add(new KeyRotateBehavior(definition.RotateLeftActionId, definition.RotateRightActionId, definition.RotateDegPerSecond));
                    break;
                case CameraRotateMode.Both:
                    behaviors.Add(new DragRotateBehavior(
                        definition.RotateHoldActionId, definition.LookActionId,
                        definition.RotateDegPerPixel, definition.MinPitchDeg, definition.MaxPitchDeg));
                    behaviors.Add(new KeyRotateBehavior(definition.RotateLeftActionId, definition.RotateRightActionId, definition.RotateDegPerSecond));
                    break;
            }

            return new CompositeCameraController(behaviors.ToArray(), ctx);
        }
    }
}
