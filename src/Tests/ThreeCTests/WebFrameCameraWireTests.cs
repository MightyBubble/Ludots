using System.Numerics;
using Ludots.Adapter.Web.Streaming;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebFrameCameraWireTests
    {
        private static readonly CameraRenderState3D DistantOverviewCamera = new(
            new Vector3(100f, 28845f, 8185.8f),
            new Vector3(100f, 285.6f, 3150f),
            Vector3.UnitY,
            50f);

        [Test]
        public void FullFrame_CarriesCoreResolvedClipPlanes_ForCameraBeyondDefaultFarPlane()
        {
            var encoder = new BinaryFrameEncoder();
            encoder.Encode(1, 1, 0, in DistantOverviewCamera, null, null, null, null, null, null);

            AssertCarriesCoreClipPlanes(WebWireClient.FrameSummary.Parse(encoder.GetResult()));
        }

        [Test]
        public void DeltaFrame_CarriesCoreResolvedClipPlanes_ForCameraBeyondDefaultFarPlane()
        {
            var compressor = new DeltaCompressor();
            compressor.TryEncodeDelta(1, 1, 0, in DistantOverviewCamera, null, null, null, null, null);
            var bytes = new byte[compressor.EncodedLength];
            compressor.CopyTo(bytes);

            AssertCarriesCoreClipPlanes(WebWireClient.FrameSummary.Parse(bytes));
        }

        private static void AssertCarriesCoreClipPlanes(WebWireClient.FrameSummary frame)
        {
            WebWireClient.WireCamera camera = frame.Camera ?? throw new AssertionException("Frame must carry the camera section.");
            CameraClipPlanes expected = DistantOverviewCamera.ResolveClipPlanes();
            float distance = Vector3.Distance(DistantOverviewCamera.Position, DistantOverviewCamera.Target);
            Assert.Multiple(() =>
            {
                Assert.That(camera.State.FovYDeg, Is.EqualTo(DistantOverviewCamera.FovYDeg));
                Assert.That(camera.NearMeters, Is.EqualTo(expected.NearMeters));
                Assert.That(camera.FarMeters, Is.EqualTo(expected.FarMeters));
                Assert.That(camera.FarMeters, Is.GreaterThan(distance),
                    "The streamed far plane must reach the camera target, or the client clips the whole scene.");
            });
        }
    }
}
