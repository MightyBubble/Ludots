using Ludots.Adapter.Raylib;
using Ludots.Adapter.Raylib.Rendering;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibFrameRendererTests
{
    private static RaylibFramePassPlanInput PlanInput(
        bool drawDebugGuides = true,
        bool drawTerrain = true,
        bool drawContinuousHeightmap = false,
        bool waterEnabled = false,
        bool hasShadowFrame = false,
        bool hasGlobalFieldBuffer = true,
        bool drawFieldOverlays = true,
        bool hasBenchmarkRenderer = true,
        bool drawPrimitives = true,
        bool hasGroundOverlays = true,
        bool hasSplineRibbons = true,
        bool hasTrailMeshes = true,
        bool drawNavMeshOverlay = false,
        bool drawDebugDraw = true,
        bool drawSkiaUi = true,
        bool drawEnvironment = true,
        bool usePostProcess = true)
    {
        return new RaylibFramePassPlanInput(
            drawDebugGuides,
            drawTerrain,
            drawContinuousHeightmap,
            waterEnabled,
            hasShadowFrame,
            hasGlobalFieldBuffer,
            drawFieldOverlays,
            hasBenchmarkRenderer,
            drawPrimitives,
            hasGroundOverlays,
            hasSplineRibbons,
            hasTrailMeshes,
            drawNavMeshOverlay,
            drawDebugDraw,
            drawSkiaUi,
            drawEnvironment,
            usePostProcess);
    }

    [Test]
    public void BuildPassPlan_KeepsWorldPassesBeforeUiComposite()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[32];
        int count = RaylibFrameRenderer.BuildPassPlan(PlanInput(), passes);

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
            RaylibFramePass.TrailMeshes,
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
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[32];
        int count = RaylibFrameRenderer.BuildPassPlan(PlanInput(
            drawDebugGuides: false,
            drawTerrain: false,
            hasGlobalFieldBuffer: false,
            hasBenchmarkRenderer: false,
            drawPrimitives: false,
            hasGroundOverlays: false,
            hasSplineRibbons: false,
            hasTrailMeshes: false,
            drawDebugDraw: false,
            drawSkiaUi: false,
            drawEnvironment: false,
            usePostProcess: false), passes);

        Assert.That(passes[..count].ToArray(), Is.EqualTo(new[]
        {
            RaylibFramePass.Clear,
            RaylibFramePass.BeginWorld3D,
            RaylibFramePass.EndWorld3D,
            RaylibFramePass.OverlayComposite,
        }));
    }

    [Test]
    public void BuildPassPlan_WaterFrameRunsReflectionRefractionBeforeWorldAndKeepsPostProcess()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[32];
        int count = RaylibFrameRenderer.BuildPassPlan(
            PlanInput(waterEnabled: true, hasShadowFrame: true, usePostProcess: true),
            passes);
        RaylibFramePass[] plan = passes[..count].ToArray();

        Assert.That(plan, Does.Contain(RaylibFramePass.WaterReflection));
        Assert.That(plan, Does.Contain(RaylibFramePass.WaterRefraction));
        Assert.That(plan, Does.Contain(RaylibFramePass.BeginWorldTexture),
            "水面帧必须保留后处理调色（旧宿主互斥是 RT 绑定丢失的规避，不是能力边界）");
        Assert.That(plan, Does.Contain(RaylibFramePass.PostProcessComposite));
        Assert.That(Array.IndexOf(plan, RaylibFramePass.WaterReflection) + 1,
            Is.EqualTo(Array.IndexOf(plan, RaylibFramePass.WaterRefraction)), "水面双 pass 必须相邻");
        AssertIndexOrder(plan, RaylibFramePass.Clear, RaylibFramePass.WaterReflection);
        AssertIndexOrder(plan, RaylibFramePass.WaterRefraction, RaylibFramePass.ShadowDepth);
        AssertIndexOrder(plan, RaylibFramePass.ShadowDepth, RaylibFramePass.BeginWorldTexture);
        AssertIndexOrder(plan, RaylibFramePass.WaterRefraction, RaylibFramePass.BeginWorld3D);
        AssertIndexOrder(plan, RaylibFramePass.BeginWorldTexture, RaylibFramePass.BeginWorld3D);
    }

    [Test]
    public void BuildPassPlan_NavMeshOverlaySitsBetweenTerrainAndGlobalField()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[32];
        int count = RaylibFrameRenderer.BuildPassPlan(PlanInput(drawNavMeshOverlay: true), passes);
        RaylibFramePass[] plan = passes[..count].ToArray();

        Assert.That(Array.IndexOf(plan, RaylibFramePass.Terrain),
            Is.LessThan(Array.IndexOf(plan, RaylibFramePass.NavMeshOverlay)));
        Assert.That(Array.IndexOf(plan, RaylibFramePass.NavMeshOverlay),
            Is.LessThan(Array.IndexOf(plan, RaylibFramePass.GlobalField)));
    }

    [Test]
    public void BuildPassPlan_HoldsOrderInvariantsAcrossFlagCombinations()
    {
        bool[] values = { false, true };
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[32];
        int combinationsChecked = 0;
        foreach (bool usePostProcess in values)
        foreach (bool waterEnabled in values)
        foreach (bool hasShadowFrame in values)
        foreach (bool drawEnvironment in values)
        foreach (bool drawTerrain in values)
        foreach (bool drawPrimitives in values)
        foreach (bool drawNavMeshOverlay in values)
        foreach (bool drawSkiaUi in values)
        foreach (bool drawDebugDraw in values)
        {
            int count = RaylibFrameRenderer.BuildPassPlan(
                PlanInput(
                                    drawTerrain: drawTerrain,
                                    waterEnabled: waterEnabled,
                                    hasShadowFrame: hasShadowFrame,
                                    drawPrimitives: drawPrimitives,
                                    drawNavMeshOverlay: drawNavMeshOverlay,
                                    drawDebugDraw: drawDebugDraw,
                                    drawSkiaUi: drawSkiaUi,
                                    drawEnvironment: drawEnvironment,
                                    usePostProcess: usePostProcess),
                passes);
            RaylibFramePass[] plan = passes[..count].ToArray();
            combinationsChecked++;

            Assert.That(plan[0], Is.EqualTo(RaylibFramePass.Clear), $"首 pass 必须是 Clear：{Format(plan)}");
            Assert.That(plan[^1], Is.EqualTo(RaylibFramePass.OverlayComposite), $"末 pass 必须是 OverlayComposite：{Format(plan)}");
            Assert.That(plan.Distinct().Count(), Is.EqualTo(plan.Length), $"pass 不得重复：{Format(plan)}");
            AssertIndexOrder(plan, RaylibFramePass.BeginWorld3D, RaylibFramePass.EndWorld3D);
            AssertIndexOrder(plan, RaylibFramePass.EndWorld3D, RaylibFramePass.OverlayComposite);

            if (usePostProcess)
            {
                AssertIndexOrder(plan, RaylibFramePass.Clear, RaylibFramePass.BeginWorldTexture);
                AssertIndexOrder(plan, RaylibFramePass.BeginWorldTexture, RaylibFramePass.BeginWorld3D);
                AssertIndexOrder(plan, RaylibFramePass.EndWorld3D, RaylibFramePass.PostProcessComposite);
                AssertIndexOrder(plan, RaylibFramePass.PostProcessComposite, RaylibFramePass.OverlayComposite);
            }

            if (waterEnabled)
            {
                AssertIndexOrder(plan, RaylibFramePass.Clear, RaylibFramePass.WaterReflection);
                AssertIndexOrder(plan, RaylibFramePass.WaterReflection, RaylibFramePass.WaterRefraction);
                AssertIndexOrder(plan, RaylibFramePass.WaterRefraction, RaylibFramePass.BeginWorld3D);
                if (usePostProcess)
                {
                    AssertIndexOrder(plan, RaylibFramePass.WaterRefraction, RaylibFramePass.BeginWorldTexture);
                }
            }

            if (hasShadowFrame)
            {
                AssertIndexOrder(plan, RaylibFramePass.Clear, RaylibFramePass.ShadowDepth);
                AssertIndexOrder(plan, RaylibFramePass.ShadowDepth, RaylibFramePass.BeginWorld3D);
                if (usePostProcess)
                {
                    AssertIndexOrder(plan, RaylibFramePass.ShadowDepth, RaylibFramePass.BeginWorldTexture);
                }
            }

            if (drawEnvironment)
            {
                AssertIndexOrder(plan, RaylibFramePass.BeginWorld3D, RaylibFramePass.Skybox);
            }

            if (drawNavMeshOverlay)
            {
                AssertIndexOrder(plan, RaylibFramePass.BeginWorld3D, RaylibFramePass.NavMeshOverlay);
                AssertIndexOrder(plan, RaylibFramePass.NavMeshOverlay, RaylibFramePass.EndWorld3D);
            }

            if (drawSkiaUi)
            {
                AssertIndexOrder(plan, RaylibFramePass.EndWorld3D, RaylibFramePass.BrowserLayer);
                AssertIndexOrder(plan, RaylibFramePass.BrowserLayer, RaylibFramePass.OverlayComposite);
            }
        }

        Assert.That(combinationsChecked, Is.GreaterThan(500));
    }

    [Test]
    public void BuildPassPlan_FailsFastWhenOutputSpanIsTooSmall()
    {
        Span<RaylibFramePass> passes = stackalloc RaylibFramePass[1];

        InvalidOperationException? error = null;
        try
        {
            RaylibFrameRenderer.BuildPassPlan(PlanInput(), passes);
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

    private static void AssertIndexOrder(RaylibFramePass[] plan, RaylibFramePass before, RaylibFramePass after)
    {
        Assert.That(Array.IndexOf(plan, before), Is.LessThan(Array.IndexOf(plan, after)),
            $"{before} 必须先于 {after}：{Format(plan)}");
    }

    private static string Format(RaylibFramePass[] plan)
    {
        return string.Join("->", plan);
    }
}
