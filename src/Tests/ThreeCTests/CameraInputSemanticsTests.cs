using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class CameraInputSemanticsTests
    {
        [Test]
        public void VirtualCameraRuntime_DragRotate_UsesLookActionWithPositiveYUp()
        {
            var (backend, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(handler, new VirtualCameraDefinition
            {
                Id = "DragRotate",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                DistanceCm = 1000f,
                Pitch = 45f,
                FovYDeg = 60f,
                Yaw = 180f,
                PanMode = CameraPanMode.None,
                RotateMode = CameraRotateMode.DragRotate,
                RotateDegPerPixel = 0.28f,
                MinPitchDeg = 10f,
                MaxPitchDeg = 85f,
                EnableZoom = false,
                AllowUserInput = true
            });

            backend.MousePosition = new Vector2(320f, 240f);
            handler.Update();
            manager.CaptureVisualInput();

            backend.Buttons["<Mouse>/MiddleButton"] = true;
            backend.MousePosition = new Vector2(320f, 180f);
            handler.Update();
            manager.Update(0.016f);

            Assert.That(manager.State.Pitch, Is.GreaterThan(45f));
        }

        [Test]
        public void VirtualCameraRuntime_GrabDragPan_DragRight_MovesTargetPositiveX()
        {
            var (backend, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(handler, new VirtualCameraDefinition
            {
                Id = "GrabDrag",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                PanMode = CameraPanMode.None,
                RotateMode = CameraRotateMode.None,
                EnableGrabDrag = true,
                Yaw = 0f,
                Pitch = 60f,
                DistanceCm = 5000f,
                FovYDeg = 60f,
                EnableZoom = false,
                AllowUserInput = true
            }, new StubViewController(1920f, 1080f));

            backend.MousePosition = new Vector2(400f, 300f);
            handler.Update();
            manager.CaptureVisualInput();

            backend.Buttons["<Mouse>/MiddleButton"] = true;
            backend.MousePosition = new Vector2(440f, 300f);
            handler.Update();
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm.X, Is.GreaterThan(0f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_GrabDragPan_DragUp_MovesTargetNegativeY()
        {
            var (backend, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(handler, new VirtualCameraDefinition
            {
                Id = "GrabDrag",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                PanMode = CameraPanMode.None,
                RotateMode = CameraRotateMode.None,
                EnableGrabDrag = true,
                Yaw = 0f,
                Pitch = 60f,
                DistanceCm = 5000f,
                FovYDeg = 60f,
                EnableZoom = false,
                AllowUserInput = true
            }, new StubViewController(1920f, 1080f));

            backend.MousePosition = new Vector2(400f, 300f);
            handler.Update();
            manager.CaptureVisualInput();

            backend.Buttons["<Mouse>/MiddleButton"] = true;
            backend.MousePosition = new Vector2(400f, 260f);
            handler.Update();
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.LessThan(0f));
        }

        [Test]
        public void VirtualCameraRuntime_EdgePan_UserInputSuppressed_DoesNotMoveUntilReleased()
        {
            var (backend, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(handler, new VirtualCameraDefinition
            {
                Id = "EdgePanSuppressed",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                PanMode = CameraPanMode.EdgePan,
                EdgePanMarginPx = 10f,
                EdgePanSpeedCmPerSec = 8000f,
                RotateMode = CameraRotateMode.None,
                DistanceCm = 5000f,
                Pitch = 60f,
                FovYDeg = 60f,
                Yaw = 180f,
                EnableZoom = false,
                AllowUserInput = true
            }, new StubViewController(1920f, 1080f));

            backend.MousePosition = new Vector2(0f, 0f);
            handler.Update();
            manager.SetUserInputSuppressed(true);
            manager.CaptureVisualInput();
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));

            handler.Update();
            manager.SetUserInputSuppressed(false);
            manager.CaptureVisualInput();
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.Length(), Is.GreaterThan(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_EdgePan_PointerOutsideViewport_DoesNotMove_WhenRequireInsideViewportEnabled()
        {
            var (backend, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(handler, new VirtualCameraDefinition
            {
                Id = "EdgePanRequiresInsideViewport",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                PanMode = CameraPanMode.EdgePan,
                EdgePanMarginPx = 10f,
                EdgePanSpeedCmPerSec = 8000f,
                EdgePanRequiresPointerInsideViewport = true,
                RotateMode = CameraRotateMode.None,
                DistanceCm = 5000f,
                Pitch = 60f,
                FovYDeg = 60f,
                Yaw = 180f,
                EnableZoom = false,
                AllowUserInput = true
            }, new StubViewController(1920f, 1080f));

            backend.MousePosition = new Vector2(-40f, 100f);
            handler.Update();
            manager.CaptureVisualInput();
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_EdgePan_PointerOutsideViewport_CanMove_WhenRequireInsideViewportDisabled()
        {
            var (backend, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(handler, new VirtualCameraDefinition
            {
                Id = "EdgePanAllowsOutsideViewport",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                PanMode = CameraPanMode.EdgePan,
                EdgePanMarginPx = 10f,
                EdgePanSpeedCmPerSec = 8000f,
                EdgePanRequiresPointerInsideViewport = false,
                RotateMode = CameraRotateMode.None,
                DistanceCm = 5000f,
                Pitch = 60f,
                FovYDeg = 60f,
                Yaw = 180f,
                EnableZoom = false,
                AllowUserInput = true
            }, new StubViewController(1920f, 1080f));

            backend.MousePosition = new Vector2(-40f, 100f);
            handler.Update();
            manager.CaptureVisualInput();
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.Length(), Is.GreaterThan(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_TargetConfine_ClampsToWorldBoundsPlusPadding()
        {
            var (_, handler) = BuildCameraInputHandler();
            var manager = CreateCameraManager(
                handler,
                new VirtualCameraDefinition
                {
                    Id = "WorldConfined",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    PanMode = CameraPanMode.None,
                    RotateMode = CameraRotateMode.None,
                    DistanceCm = 5000f,
                    Pitch = 60f,
                    FovYDeg = 60f,
                    Yaw = 180f,
                    EnableZoom = false,
                    AllowUserInput = true,
                    ConfineTargetToWorldBounds = true,
                    ConfinePaddingCm = 250f
                },
                new StubViewController(1920f, 1080f),
                () => new WorldAabbCm(-1000, -500, 2000, 1000));

            manager.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "WorldConfined",
                TargetCm = new Vector2(1600f, -900f)
            });

            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(1250f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(-750f).Within(0.01f));
        }

        private static (StubInputBackend backend, PlayerInputHandler handler) BuildCameraInputHandler()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "PointerDelta", Type = InputActionType.Axis2D },
                    new() { Id = "Look", Type = InputActionType.Axis2D },
                    new() { Id = "OrbitRotateHold", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Camera",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos" },
                            new() { ActionId = "PointerDelta", Path = "<Mouse>/Delta" },
                            new()
                            {
                                ActionId = "Look",
                                Path = "<Mouse>/Delta",
                                Processors = new List<InputModifierDef>
                                {
                                    new()
                                    {
                                        Type = "Invert",
                                        Parameters = new List<InputParameterDef> { new() { Name = "Y", Value = 1f } }
                                    }
                                }
                            },
                            new() { ActionId = "OrbitRotateHold", Path = "<Mouse>/MiddleButton" }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Camera");
            return (backend, handler);
        }

        private static CameraManager CreateCameraManager(
            PlayerInputHandler handler,
            VirtualCameraDefinition definition,
            IViewController? viewController = null,
            Func<WorldAabbCm>? targetBoundsProvider = null)
        {
            var manager = new CameraManager();
            var registry = new VirtualCameraRegistry();
            registry.Register(definition);
            manager.SetVirtualCameraRegistry(registry);
            manager.ConfigureRuntime(handler, viewController ?? new StubViewController(), targetBoundsProvider);
            manager.ActivateVirtualCamera(definition.Id, blendDurationSeconds: 0f);
            return manager;
        }

        private sealed class StubInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new();
            public Vector2 MousePosition { get; set; }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width = 1280f, float height = 720f, float fov = 60f)
            {
                Resolution = new Vector2(width, height);
                Fov = fov;
            }

            public Vector2 Resolution { get; }
            public float Fov { get; }
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }
    }
}
