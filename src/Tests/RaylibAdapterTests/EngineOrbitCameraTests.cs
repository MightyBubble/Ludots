using System.Numerics;
using Ludots.App.RaylibEngineGallery;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [Category("raylib-field")]
    public sealed class EngineOrbitCameraTests
    {
        [Test]
        public void Reset_UsesProvidedDefaults()
        {
            Vector3 initialTarget = new(1.5f, -2.0f, 3.25f);
            var camera = new EngineOrbitCamera(70f, 10f, 20f, initialTarget, 50f);

            Assert.That(camera.Target, Is.EqualTo(initialTarget));
            AssertCameraPose(camera.Camera, 70f, 10f, 20f, initialTarget, 50f);

            Vector3 updatedTarget = new(-4f, 5.5f, 6.25f);
            camera.Reset(80f, 15f, 25f, updatedTarget, 55f);

            Assert.That(camera.Target, Is.EqualTo(updatedTarget));
            AssertCameraPose(camera.Camera, 80f, 15f, 25f, updatedTarget, 55f);
        }

        private static void AssertCameraPose(
            Raylib_cs.Camera3D camera,
            float distance,
            float pitchDeg,
            float yawDeg,
            Vector3 target,
            float fovy)
        {
            Vector3 expectedPosition = new(
                target.X + distance * MathF.Cos(yawDeg * MathF.PI / 180f) * MathF.Cos(pitchDeg * MathF.PI / 180f),
                target.Y + distance * MathF.Sin(pitchDeg * MathF.PI / 180f),
                target.Z + distance * MathF.Sin(yawDeg * MathF.PI / 180f) * MathF.Cos(pitchDeg * MathF.PI / 180f));

            Assert.That(camera.position.X, Is.EqualTo(expectedPosition.X).Within(0.0001f));
            Assert.That(camera.position.Y, Is.EqualTo(expectedPosition.Y).Within(0.0001f));
            Assert.That(camera.position.Z, Is.EqualTo(expectedPosition.Z).Within(0.0001f));
            Assert.That(camera.target, Is.EqualTo(target));
            Assert.That(camera.fovy, Is.EqualTo(fovy).Within(0.0001f));
        }
    }
}
