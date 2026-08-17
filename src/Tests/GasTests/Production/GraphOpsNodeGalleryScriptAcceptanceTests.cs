using System;
using System.Linq;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryScriptAcceptanceTests
{
    [Test]
    public void ScriptVignettes_CompileWithFeaturedOp()
    {
        string[] ops =
        [
            "Jump", "JumpIfFalse", "Call", "Return", "Yield",
            "HaltReturnInt", "InvokeScript", "MoveInt"
        ];
        string assets = GraphOpsNodeGalleryRuntime.ResolveAssetsRoot();
        foreach (string op in ops)
        {
            GraphOpsNodeVignette vignette = GraphOpsNodeVignetteLoader.Load(assets, op);
            var compiled = GraphOpsNodeGraphCompiler.Compile(assets, vignette);
            Assert.That(compiled.Succeeded, Is.True, op);
            Assert.That(
                compiled.Program.Any(i => i.Op == (ushort)Enum.Parse<GraphNodeOp>(op)),
                Is.True,
                $"{op} compiled program must emit the featured opcode.");
        }
    }

    [Test]
    public void ScriptOps_SmokeTick_SubstitutesCaption()
    {
        string[] ops = ["Jump", "Call", "Return", "InvokeScript", "MoveInt"];
        foreach (string op in ops)
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp(op);
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++)
            {
                runtime.Tick(0.35f);
            }

            AssertBannedPlayerCopy(runtime.Metrics.Detail);
            Assert.That(runtime.Metrics.Detail, Does.Not.Contain("{"));
            foreach (string phrase in runtime.Vignette.AssertDetailContains)
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), op);
            }
        }
    }

    [Test]
    public void Yield_FillsWaterAcrossTicks_AndCaptionRests()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("Yield");
        runtime.EnsureWorld();
        float opening = runtime.Context.ActorHealth[0];

        for (int i = 0; i < 12; i++)
        {
            runtime.Tick(0.2f);
        }

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("续一杯，歇一口气"));
        Assert.That(runtime.Context.ActorHealth[0], Is.EqualTo(opening).Within(0.01f));
        Assert.That(int.Parse(runtime.Context.CaptionValues["water"]), Is.GreaterThan(0));
        Assert.That(runtime.Metrics.Detail, Does.Contain("茶水"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("歇"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void HaltReturnInt_CaptionReportsSeven()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("HaltReturnInt");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("算出一个整数就收工"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("七").Or.Contain("7"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contain("Halt"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void JumpIfFalse_TeaFillsAcrossTicks()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("JumpIfFalse");
        runtime.EnsureWorld();
        float openingHealth = runtime.Context.ActorHealth[0];

        for (int i = 0; i < 4; i++)
        {
            runtime.Tick(0.2f);
        }

        int midWater = int.Parse(runtime.Context.CaptionValues["water"]);
        for (int i = 0; i < 8; i++)
        {
            runtime.Tick(0.2f);
        }

        int laterWater = int.Parse(runtime.Context.CaptionValues["water"]);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("没满就再续一杯"));
        Assert.That(runtime.Context.ActorHealth[0], Is.EqualTo(openingHealth).Within(0.01f));
        Assert.That(midWater, Is.GreaterThan(0));
        Assert.That(laterWater, Is.GreaterThanOrEqualTo(midWater));
        Assert.That(runtime.Metrics.Detail, Does.Contain("茶水"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("再续"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void ScriptOps_DrawOverlayDoesNotThrow()
    {
        string[] ops =
        [
            "Jump", "JumpIfFalse", "Call", "Return", "Yield",
            "HaltReturnInt", "InvokeScript", "MoveInt"
        ];
        foreach (string op in ops)
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp(op);
            runtime.EnsureWorld();
            for (int i = 0; i < 6; i++)
            {
                runtime.Tick(0.35f);
            }

            var debugDraw = new Ludots.Platform.Abstractions.DebugDrawCommandBuffer();
            runtime.DrawOverlay(debugDraw);
            Assert.That(debugDraw.Lines.Count + debugDraw.Circles.Count + debugDraw.Boxes.Count, Is.GreaterThan(0), op);
        }
    }

    private static void AssertBannedPlayerCopy(string detail)
    {
        Assert.That(detail, Does.Not.Contain("tally"));
        Assert.That(detail, Does.Not.Contain("Validation"));
        Assert.That(detail, Does.Not.Contain("FuncLib"));
        Assert.That(detail, Does.Not.Contain("True"));
        Assert.That(detail, Does.Not.Contain("False"));
        Assert.That(detail, Does.Not.Contain("耗时"));
        Assert.That(detail, Does.Not.Contain("GraphNodeOp"));
    }
}
