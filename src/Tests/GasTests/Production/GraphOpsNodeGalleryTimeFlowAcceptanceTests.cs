using System;
using System.IO;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryTimeFlowAcceptanceTests
{
    [Test]
    public void ReadTimeFlowPaused_ShowsTheSimulationIsRunning()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadTimeFlowPaused");
        TimeFlowService flow = LiveFlow();
        GraphOpsNodeVignetteLoader.RejectBannedCaption(runtime.Metrics.Detail, runtime.Op, "detail");
        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        Assert.That(runtime.Metrics.Detail, Does.Contain("整局没有停"));
    }

    [Test]
    public void ReadTimeFlowScalePermille_ShowsTheLiveScale()
    {
        using GraphOpsNodeGalleryRuntime runtime = new();
        runtime.BindOp("ReadTimeFlowScalePermille");
        runtime.EnsureWorld();
        TimeFlowService flow = LiveFlow();
        int scale = flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation);
        runtime.Tick(0.35f);
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(scale));
        Assert.That(runtime.Metrics.Detail, Does.Contain(scale.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("整局倍率"));
    }

    [Test]
    public void AcquireTimeFlowPause_PausesThenReleases()
    {
        using GraphOpsNodeGalleryRuntime runtime = new();
        runtime.BindOp("AcquireTimeFlowPause");
        runtime.EnsureWorld();
        TimeFlowService flow = LiveFlow();
        int before = flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation);
        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        runtime.Tick(0.35f);
        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain("暂停令牌"));
        Assert.That(runtime.Metrics.Detail, Does.Match(@"暂停令牌 [1-9][0-9]*"));
    }

    [Test]
    public void AcquireTimeFlowScale_ReadsTwoThousandThenReleases()
    {
        using GraphOpsNodeGalleryRuntime runtime = new();
        runtime.BindOp("AcquireTimeFlowScale");
        runtime.EnsureWorld();
        TimeFlowService flow = LiveFlow();
        int before = flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation);
        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        runtime.Tick(0.35f);
        long held = (long)before * 2000 / TimeFlowService.DefaultScalePermille;
        if (held > TimeFlowService.MaxScalePermille)
        {
            held = TimeFlowService.MaxScalePermille;
        }

        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain(held.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("这一拍读到"));
    }

    [Test]
    public void ReleaseTimeFlowToken_ReleasesThePauseItJustTook()
    {
        using GraphOpsNodeGalleryRuntime runtime = new();
        runtime.BindOp("ReleaseTimeFlowToken");
        runtime.EnsureWorld();
        TimeFlowService flow = LiveFlow();
        int before = flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation);
        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        runtime.Tick(0.35f);
        Assert.That(flow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain("世界继续走"));
    }

    private static GraphOpsNodeGalleryRuntime BindAndTick(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
    }

    private static TimeFlowService LiveFlow()
    {
        GameEngine engine = GraphOpsHeadlessGameEngine.SharedGallery(FindRepoRoot());
        return engine.GetService(CoreServiceKeys.TimeFlow)
            ?? throw new InvalidOperationException("TimeFlow missing from the shared gallery engine.");
    }

    private static string FindRepoRoot()
    {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
