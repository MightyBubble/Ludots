using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Utils;
using NUnit.Framework;

namespace GasTests
{
    public sealed class CameraScreenTransformTests
    {
        private static readonly Vector2 Resolution = new(1280f, 720f);
        private const float AspectRatio = 1280f / 720f;

        [Test]
        public void CameraViewportUtil_ScreenToRay_RoundTripsProjectedGroundPoint()
        {
            var state = CreateRtsCameraState();
            var renderState = CameraViewportUtil.StateToRenderState(state);
            var worldPoint = new Vector3(2.25f, 0f, -5.5f);

            var screen = CameraViewportUtil.WorldToScreen(worldPoint, renderState, Resolution, AspectRatio);

            Assert.That(float.IsNaN(screen.X), Is.False);
            Assert.That(float.IsNaN(screen.Y), Is.False);

            var ray = CameraViewportUtil.ScreenToRay(screen, renderState, Resolution, AspectRatio);

            Assert.That(GroundRaycastUtil.TryGetGroundVisualMeters(in ray, out var hitPoint), Is.True);
            Assert.That(Vector2.Distance(new Vector2(worldPoint.X, worldPoint.Z), new Vector2(hitPoint.X, hitPoint.Z)), Is.LessThan(0.05f));
        }

        [Test]
        public void CoreScreenRayProvider_UsesLogicalCameraState_BeforePresenterProducesRenderState()
        {
            var cameraManager = new CameraManager();
            cameraManager.State.TargetCm = Vector2.Zero;
            cameraManager.State.DistanceCm = 4000f;
            cameraManager.State.Pitch = 56f;
            cameraManager.State.Yaw = 180f;
            cameraManager.State.FovYDeg = 50f;

            var provider = new CoreScreenRayProvider(cameraManager, new StubViewController(Resolution, AspectRatio));

            var ray = provider.GetRay(new Vector2(Resolution.X * 0.5f, Resolution.Y * 0.5f));

            Assert.That(GroundRaycastUtil.TryGetGroundVisualMeters(in ray, out var hitPoint), Is.True);
            Assert.That(hitPoint.X, Is.EqualTo(0f).Within(0.05f));
            Assert.That(hitPoint.Z, Is.EqualTo(0f).Within(0.05f));
        }

        private static CameraState CreateRtsCameraState()
        {
            return new CameraState
            {
                TargetCm = Vector2.Zero,
                DistanceCm = 4000f,
                Pitch = 56f,
                Yaw = 180f,
                FovYDeg = 50f,
            };
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(Vector2 resolution, float aspectRatio)
            {
                Resolution = resolution;
                AspectRatio = aspectRatio;
            }

            public Vector2 Resolution { get; }

            public float Fov => 50f;

            public float AspectRatio { get; }
        }
    }
}
