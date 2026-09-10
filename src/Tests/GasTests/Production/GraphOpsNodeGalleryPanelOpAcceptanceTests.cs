using System;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryPanelOpAcceptanceTests
{
    [Test]
    public void ShowMinimapVignette_LightsTheNativeMinimap()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ShowMinimap");
        runtime.EnsureWorld();

        GameEngine engine = GraphOpsHeadlessGameEngine.SharedGallery(
            GraphOpsHeadlessGameEngine.FindRepoRoot(runtime.Context.AssetsRoot));
        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("Gallery engine is missing MinimapRuntime.");
        minimap.Visible = false;
        minimap.UseFollowCameraPreset();
        minimap.SetRotateWithCamera(true);

        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"ShowMinimap detail missing phrase: {runtime.Metrics.Detail}");
        }

        Assert.That(minimap.Visible, Is.True, "ShowMinimap must turn the native minimap on.");
        Assert.That(minimap.Preset, Is.EqualTo(MinimapPreset.RtsFullMap));
        Assert.That(minimap.RotateWithCamera, Is.False);
    }

    [Test]
    public void ShowPanelVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ShowPanel");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"ShowPanel detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void HidePanelVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("HidePanel");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"HidePanel detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void CreatePanelVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("CreatePanel");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"CreatePanel detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void SetWorldPositionVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SetWorldPosition");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"SetWorldPosition detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void SetInteractionModeVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SetInteractionMode");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"SetInteractionMode detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void SetPanelAudienceVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SetPanelAudience");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"SetPanelAudience detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void SpawnTemplateVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SpawnTemplate");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"SpawnTemplate detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void DestroyPanelVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("DestroyPanel");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"DestroyPanel detail missing phrase: {runtime.Metrics.Detail}");
        }
    }
}
