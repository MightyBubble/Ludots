using Ludots.Adapter.Raylib;
using Ludots.Adapter.Raylib.Rendering;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibFrameRendererTests
{
    [Test]
    public void BuildPassPlan_KeepsWorldPassesBeforeUiComposite()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[16];
        int count = RaylibFrameRenderer.BuildPassPlan(
            new RaylibFramePassPlanInput(
                DrawDebugGuides: true,
                DrawTerrain: true,
                DrawVisualHeightmap: false,
                HasGlobalFieldBuffer: true,
                DrawFieldOverlays: true,
                HasBenchmarkRenderer: true,
                DrawPrimitives: true,
                HasGroundOverlays: true,
                HasSplineRibbons: true,
                DrawDebugDraw: true,
                DrawSkiaUi: true,
                DrawEnvironment: true,
                UsePostProcess: true),
            passes);

        Assert.That(passes[..count].ToArray(), Is.EqualTo(new[]
        {
            RaylibFramePass.Clear,
            RaylibFramePass.BeginWorldTexture,
            RaylibFramePass.BeginWorld3D,
            RaylibFramePass.Skybox,
            RaylibFramePass.DebugGuides,
            RaylibFramePass.Terrain,
            RaylibFramePass.GlobalField,
            RaylibFramePass.BenchmarkScene,
            RaylibFramePass.PrimitiveVisuals,
            RaylibFramePass.GroundOverlay,
            RaylibFramePass.SplineRibbon,
            RaylibFramePass.DebugDraw,
            RaylibFramePass.EndWorld3D,
            RaylibFramePass.PostProcessComposite,
            RaylibFramePass.BrowserLayer,
            RaylibFramePass.OverlayComposite,
        }));
    }

    [Test]
    public void BuildPassPlan_CanSuppressOptionalPassesWithoutMovingOverlay()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[16];
        int count = RaylibFrameRenderer.BuildPassPlan(
            new RaylibFramePassPlanInput(
                DrawDebugGuides: false,
                DrawTerrain: false,
                DrawVisualHeightmap: false,
                HasGlobalFieldBuffer: false,
                DrawFieldOverlays: true,
                HasBenchmarkRenderer: false,
                DrawPrimitives: false,
                HasGroundOverlays: false,
                HasSplineRibbons: false,
                DrawDebugDraw: false,
                DrawSkiaUi: false,
                DrawEnvironment: false,
                UsePostProcess: false),
            passes);

        Assert.That(passes[..count].ToArray(), Is.EqualTo(new[]
        {
            RaylibFramePass.Clear,
            RaylibFramePass.BeginWorld3D,
            RaylibFramePass.EndWorld3D,
            RaylibFramePass.OverlayComposite,
        }));
    }

    [Test]
    public void BuildPassPlan_FailsFastWhenOutputSpanIsTooSmall()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[1];

        InvalidOperationException? error = null;
        try
        {
            RaylibFrameRenderer.BuildPassPlan(
                new RaylibFramePassPlanInput(
                    DrawDebugGuides: true,
                    DrawTerrain: true,
                    DrawVisualHeightmap: true,
                    HasGlobalFieldBuffer: true,
                    DrawFieldOverlays: true,
                    HasBenchmarkRenderer: true,
                    DrawPrimitives: true,
                    HasGroundOverlays: true,
                    HasSplineRibbons: true,
                    DrawDebugDraw: true,
                    DrawSkiaUi: true,
                    DrawEnvironment: true,
                    UsePostProcess: true),
                passes);
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }

        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void BindInstancedBatchLaneSource_EmptyStoreBindsWithoutResidentLanes()
    {
        var store = new RaylibInstancedBatchLaneStore();
        using var renderer = new RaylibPrimitiveRenderer();

        Assert.DoesNotThrow(() => renderer.BindInstancedBatchLaneSource(store));
        Assert.That(store.ResidentLaneCount, Is.EqualTo(0));
        Assert.That(store.LastAppliedRequestCount, Is.EqualTo(0));
        Assert.Throws<ArgumentNullException>(() => renderer.BindInstancedBatchLaneSource(null!));
    }
}
