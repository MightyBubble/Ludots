using System;
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
                    definition.ZoomCmPerWheel,
                    definition.MinDistanceCm, definition.MaxDistanceCm));
            }

            switch (definition.PanMode)
            {
                case CameraPanMode.Keyboard:
                    behaviors.Add(new KeyboardPanBehavior(definition.PanCmPerSecond));
                    break;
                case CameraPanMode.EdgePan:
                    behaviors.Add(new EdgePanBehavior(
                        definition.EdgePanMarginPx,
                        definition.EdgePanSpeedCmPerSec,
                        definition.EdgePanRequiresPointerInsideViewport));
                    break;
                case CameraPanMode.KeyboardAndEdge:
                    behaviors.Add(new KeyboardPanBehavior(definition.PanCmPerSecond));
                    behaviors.Add(new EdgePanBehavior(
                        definition.EdgePanMarginPx,
                        definition.EdgePanSpeedCmPerSec,
                        definition.EdgePanRequiresPointerInsideViewport));
                    break;
                case CameraPanMode.None:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Virtual camera '{definition.Id}' declares unsupported pan mode '{definition.PanMode}'.");
            }

            if (definition.EnableGrabDrag)
            {
                behaviors.Add(new GrabDragPanBehavior());
            }

            switch (definition.RotateMode)
            {
                case CameraRotateMode.DragRotate:
                    behaviors.Add(new DragRotateBehavior(
                        definition.RotateDegPerPixel, definition.MinPitchDeg, definition.MaxPitchDeg));
                    break;
                case CameraRotateMode.KeyRotate:
                    behaviors.Add(new KeyRotateBehavior(definition.RotateDegPerSecond));
                    break;
                case CameraRotateMode.Both:
                    behaviors.Add(new DragRotateBehavior(
                        definition.RotateDegPerPixel, definition.MinPitchDeg, definition.MaxPitchDeg));
                    behaviors.Add(new KeyRotateBehavior(definition.RotateDegPerSecond));
                    break;
                case CameraRotateMode.None:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Virtual camera '{definition.Id}' declares unsupported rotate mode '{definition.RotateMode}'.");
            }

            return new CompositeCameraController(behaviors.ToArray(), ctx);
        }
    }
}
