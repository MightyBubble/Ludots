using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class CoreProjectionCameraSnapshotTests
    {
        [Test]
        public void ProjectionServices_UsePresenterOutsidePresentationAndAlphaSnapshotInsidePresentation()
        {
            var manager = new CameraManager();
            manager.PreviousState.Yaw = 45f;
            manager.PreviousState.Pitch = 55f;
            manager.PreviousState.DistanceCm = 2800f;
            manager.PreviousState.FovYDeg = 60f;
            manager.PreviousState.TargetCm = Vector2.Zero;
            manager.State.Yaw = 45f;
            manager.State.Pitch = 55f;
            manager.State.DistanceCm = 2800f;
            manager.State.FovYDeg = 60f;
            manager.State.TargetCm = new Vector2(6000f, 0f);

            var view = new StubViewController();
            var presenter = new CameraPresenter(new StubSpatialCoordinateConverter(), new StubCameraAdapter());
            presenter.Update(manager, 0f);

            var projector = new CoreScreenProjector(manager, view);
            var rayProvider = new CoreScreenRayProvider(manager, view);
            projector.BindPresenter(presenter);
            rayProvider.BindPresenter(presenter);
            projector.BindPresentationAlphaProvider(() => 1f);
            rayProvider.BindPresentationAlphaProvider(() => 1f);

            Vector2 presenterTargetScreen = projector.WorldToScreen(
                WorldUnits.WorldCmToVisualMeters(WorldCmInt2.Zero, yMeters: 0f));
            Assert.That(float.IsFinite(presenterTargetScreen.X) && float.IsFinite(presenterTargetScreen.Y), Is.True);
            Assert.That(Vector2.Distance(presenterTargetScreen, new Vector2(960f, 540f)), Is.LessThan(1f));

            ScreenRay presenterRay = rayProvider.GetRay(new Vector2(960f, 540f));
            Assert.That(GroundRaycastUtil.TryGetGroundWorldCm(in presenterRay, out WorldCmInt2 presenterHitWorldCm), Is.True);
            Assert.That(presenterHitWorldCm.X, Is.EqualTo(0).Within(1));
            Assert.That(presenterHitWorldCm.Y, Is.EqualTo(0).Within(1));

            BeginPresentationFrame(projector);
            BeginPresentationFrame(rayProvider);
            try
            {
                Vector2 currentTargetScreen = projector.WorldToScreen(
                    WorldUnits.WorldCmToVisualMeters(new WorldCmInt2(6000, 0), yMeters: 0f));
                Assert.That(float.IsFinite(currentTargetScreen.X) && float.IsFinite(currentTargetScreen.Y), Is.True);
                Assert.That(Vector2.Distance(currentTargetScreen, new Vector2(960f, 540f)), Is.LessThan(1f));

                ScreenRay ray = rayProvider.GetRay(new Vector2(960f, 540f));
                Assert.That(GroundRaycastUtil.TryGetGroundWorldCm(in ray, out WorldCmInt2 hitWorldCm), Is.True);
                Assert.That(hitWorldCm.X, Is.EqualTo(6000).Within(1));
                Assert.That(hitWorldCm.Y, Is.EqualTo(0).Within(1));
            }
            finally
            {
                EndPresentationFrame(projector);
                EndPresentationFrame(rayProvider);
            }
        }

        private static void BeginPresentationFrame(object service)
        {
            InvokePresentationScope(service, "BeginPresentationFrame");
        }

        private static void EndPresentationFrame(object service)
        {
            InvokePresentationScope(service, "EndPresentationFrame");
        }

        private static void InvokePresentationScope(object service, string methodName)
        {
            System.Type? scope = null;
            System.Type[] interfaces = service.GetType().GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (interfaces[i].FullName == "Ludots.Core.Presentation.Camera.IPresentationCameraSnapshotScope")
                {
                    scope = interfaces[i];
                    break;
                }
            }

            Assert.That(scope, Is.Not.Null);
            scope!.GetMethod(methodName)!.Invoke(service, null);
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution => new(1920f, 1080f);
            public float Fov => 60f;
            public float AspectRatio => 1920f / 1080f;
        }

        private sealed class StubCameraAdapter : ICameraAdapter
        {
            public void UpdateCamera(in CameraRenderState3D state)
            {
            }
        }

        private sealed class StubSpatialCoordinateConverter : ISpatialCoordinateConverter
        {
            public int GridCellSizeCm => 100;
            public WorldCmInt2 GridToWorld(in IntVector2 grid) => new(grid.X * 100, grid.Y * 100);
            public IntVector2 WorldToGrid(in WorldCmInt2 world) => new(world.X / 100, world.Y / 100);
            public WorldCmInt2 HexToWorld(in HexCoordinates hex) => WorldCmInt2.Zero;
            public HexCoordinates WorldToHex(in WorldCmInt2 world) => default;
        }
    }
}
