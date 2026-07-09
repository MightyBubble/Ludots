using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Bindings;
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
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
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

            SetBehaviorInput(input, look: new Vector2(0f, 60f), rotateHold: true);
            manager.Update(0.016f);

            Assert.That(manager.State.Pitch, Is.GreaterThan(45f));
        }

        [Test]
        public void VirtualCameraRuntime_DragRotate_DefaultRequiresRotateHold()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
            {
                Id = "DragRotateRequiresHold",
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

            SetBehaviorInput(input, look: new Vector2(40f, 0f), rotateHold: false);
            manager.Update(0.016f);

            Assert.That(manager.State.Yaw, Is.EqualTo(180f).Within(0.001f));
        }

        [Test]
        public void VirtualCameraRuntime_DragRotate_CanRotateWithoutHold_WhenConfigured()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
            {
                Id = "DragRotateFreeLook",
                Priority = 0,
                RigKind = CameraRigKind.ThirdPerson,
                DistanceCm = 1000f,
                Pitch = 18f,
                FovYDeg = 60f,
                Yaw = 180f,
                PanMode = CameraPanMode.None,
                RotateMode = CameraRotateMode.DragRotate,
                RotateDegPerPixel = 0.28f,
                RotateRequiresHold = false,
                MinPitchDeg = -20f,
                MaxPitchDeg = 45f,
                EnableZoom = false,
                AllowUserInput = true
            });

            SetBehaviorInput(input, look: new Vector2(40f, 0f), rotateHold: false);
            manager.Update(0.016f);

            Assert.That(MathF.Abs(manager.State.Yaw - 180f), Is.GreaterThan(0.001f));
        }

        [Test]
        public void VirtualCameraRuntime_DragRotate_ConsumesCurrentBehaviorStateOnly()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
            {
                Id = "DragRotateBehaviorState",
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

            SetBehaviorInput(input, look: new Vector2(40f, 0f), rotateHold: true);
            manager.Update(0.016f);
            float yawAfterInput = manager.State.Yaw;

            input.Clear();
            manager.Update(0.016f);

            Assert.That(yawAfterInput, Is.Not.EqualTo(180f),
                "Camera behavior should read look from the behavior state produced by the attribute sink.");
            Assert.That(manager.State.Yaw, Is.EqualTo(yawAfterInput).Within(0.001f),
                "After the behavior state is cleared, camera rotation must not replay old input.");
        }

        [Test]
        public void VirtualCameraRuntime_GrabDragPan_DragRight_MovesTargetPositiveX()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
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

            SetBehaviorInput(input, pointerDelta: new Vector2(40f, 0f), grabDragHold: true);
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm.X, Is.GreaterThan(0f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_GrabDragPan_DragUp_MovesTargetNegativeY()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
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

            SetBehaviorInput(input, pointerDelta: new Vector2(0f, -40f), grabDragHold: true);
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.LessThan(0f));
        }

        [Test]
        public void VirtualCameraRuntime_EdgePan_ZeroBehaviorInput_DoesNotMoveUntilPointerAttributeArrives()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
            {
                Id = "EdgePanBehaviorInput",
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

            input.Clear();
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));

            SetBehaviorInput(input, pointerPosition: Vector2.Zero);
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.Length(), Is.GreaterThan(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_EdgePan_PointerOutsideViewport_DoesNotMove_WhenRequireInsideViewportEnabled()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
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

            SetBehaviorInput(input, pointerPosition: new Vector2(-40f, 100f));
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_EdgePan_PointerOutsideViewport_CanMove_WhenRequireInsideViewportDisabled()
        {
            var (manager, input) = CreateCameraManager(new VirtualCameraDefinition
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

            SetBehaviorInput(input, pointerPosition: new Vector2(-40f, 100f));
            manager.Update(1f);

            Assert.That(manager.State.TargetCm.Length(), Is.GreaterThan(0.01f));
        }

        [Test]
        public void VirtualCameraRuntime_TargetConfine_ClampsToWorldBoundsPlusPadding()
        {
            var (manager, _) = CreateCameraManager(
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

        private static (CameraManager Manager, CameraBehaviorInputState Input) CreateCameraManager(
            VirtualCameraDefinition definition,
            IViewController? viewController = null,
            Func<WorldAabbCm>? targetBoundsProvider = null)
        {
            var behaviorInput = new CameraBehaviorInputState();
            var manager = new CameraManager();
            var registry = new VirtualCameraRegistry();
            registry.Register(definition);
            manager.SetVirtualCameraRegistry(registry);
            manager.ConfigureRuntime(behaviorInput, viewController ?? new StubViewController(), targetBoundsProvider);
            manager.ActivateVirtualCamera(definition.Id, blendDurationSeconds: 0f);
            return (manager, behaviorInput);
        }

        private static void SetBehaviorInput(
            CameraBehaviorInputState input,
            Vector2? pointerPosition = null,
            Vector2? pointerDelta = null,
            Vector2? look = null,
            bool rotateHold = false,
            bool grabDragHold = false,
            float zoom = 0f,
            bool rotateLeft = false,
            bool rotateRight = false)
        {
            input.Clear();
            Vector2 pointer = pointerPosition ?? Vector2.Zero;
            Vector2 delta = pointerDelta ?? Vector2.Zero;
            Vector2 lookValue = look ?? Vector2.Zero;

            input.Apply(CameraBehaviorInputChannels.PointerX, pointer.X, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.PointerY, pointer.Y, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.PointerActive, pointerPosition.HasValue ? 1f : 0f, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.PointerDeltaX, delta.X, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.PointerDeltaY, delta.Y, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.LookX, lookValue.X, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.LookY, lookValue.Y, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.Zoom, zoom, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.RotateHold, rotateHold ? 1f : 0f, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.GrabDragHold, grabDragHold ? 1f : 0f, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.RotateLeft, rotateLeft ? 1f : 0f, AttributeBindingMode.Override);
            input.Apply(CameraBehaviorInputChannels.RotateRight, rotateRight ? 1f : 0f, AttributeBindingMode.Override);
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
