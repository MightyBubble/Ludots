using System.IO;
using DeterministicReplayShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Tests;
using NUnit.Framework;
using ReconnectRecoveryShowcaseMod.Runtime;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class DeterministicReplayShowcaseAcceptanceTests
{
    [Test]
    public void PanelState_ExposesPlayerControlsAndCompareLane()
    {
        var runtime = new DeterministicReplayShowcaseRuntime();
        var state = runtime.BuildPanelState();
        Assert.That(state.Header, Is.EqualTo("确定性回放"));
        Assert.That(state.Controls, Does.Contain("录"));
        Assert.That(state.Controls, Does.Contain("播"));
        Assert.That(state.Controls, Does.Contain("逐帧"));
        Assert.That(state.HashRows, Is.Not.Empty);
    }
}

[TestFixture]
public sealed class ReconnectRecoveryShowcaseAcceptanceTests
{
    [Test]
    public void BannerDeclaresSingleProcessSimulation_AndFaultsAreReadable()
    {
        var runtime = new ReconnectRecoveryShowcaseRuntime();
        var state = runtime.BuildPanelState();
        Assert.That(state.Banner, Does.Contain("单机模拟"));
        Assert.That(state.Banner, Does.Contain("联机专项未验收"));

        runtime.InjectMissing();
        state = runtime.BuildPanelState();
        Assert.That(state.Error, Is.Not.Null);
        Assert.That(state.LastFault, Does.Contain("缺帧"));

        runtime.InjectDuplicate();
        Assert.That(runtime.BuildPanelState().LastFault, Does.Contain("重复"));

        runtime.InjectStale();
        Assert.That(runtime.BuildPanelState().LastFault, Does.Contain("过期"));

        runtime.InjectOutOfOrder();
        Assert.That(runtime.BuildPanelState().LastFault, Does.Contain("乱序"));
    }
}
