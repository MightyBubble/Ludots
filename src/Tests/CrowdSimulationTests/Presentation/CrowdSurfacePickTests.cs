using System.Numerics;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

[TestFixture]
public sealed class CrowdSurfacePickTests
{
    [Test]
    public void UiCaptured_SuppressesPick_EvenWhenRayWouldHit()
    {
        var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.UiCaptured, true);
        engine.SetService(CoreServiceKeys.ContinuousHeightmap, new FlatGroundHeightmap());
        engine.SetService(CoreServiceKeys.ScreenRayProvider, new FixedRayProvider());
        var input = new FrozenInputActionReader();
        input.SetActionValue(ReservedInputActionIds.PointerPos, new Vector3(640f, 360f, 0f));

        Assert.That(CrowdSurfacePick.TryPick(engine, input, out double x, out double y), Is.False);
        Assert.That(x, Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(0));
    }

    [Test]
    public void UiNotCaptured_PicksGround_WhenRayHits()
    {
        var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.SetService(CoreServiceKeys.ContinuousHeightmap, new FlatGroundHeightmap());
        engine.SetService(CoreServiceKeys.ScreenRayProvider, new FixedRayProvider());
        var input = new FrozenInputActionReader();
        input.SetActionValue(ReservedInputActionIds.PointerPos, new Vector3(100f, 200f, 0f));

        Assert.That(CrowdSurfacePick.TryPick(engine, input, out double x, out double y), Is.True);
        Assert.That(x, Is.EqualTo(100));
        Assert.That(y, Is.EqualTo(200));
    }

    private sealed class FlatGroundHeightmap : IContinuousHeightmap
    {
        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = 0f;
            return true;
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            => throw new NotSupportedException();

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = new VisualGroundHit(100f, 200f, 0f, 0);
            return true;
        }

        public bool RaycastGroundBatch(
            ReadOnlySpan<float> originXMeters,
            ReadOnlySpan<float> originYMeters,
            ReadOnlySpan<float> originZMeters,
            ReadOnlySpan<float> directionX,
            ReadOnlySpan<float> directionY,
            ReadOnlySpan<float> directionZ,
            Span<float> outWorldXCm,
            Span<float> outWorldYCm,
            Span<float> outHeightCm,
            Span<float> outDistanceMeters,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outLayerIndex,
            Span<byte> outHitMask,
            int layerIndex = -1)
            => throw new NotSupportedException();
    }

    private sealed class FixedRayProvider : IScreenRayProvider
    {
        public ScreenRay GetRay(Vector2 screenPosition) => default;
    }
}
