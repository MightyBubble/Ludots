using Ludots.Core.Engine.TimeFlow;
using NUnit.Framework;

namespace Ludots.Tests.TimeFlowCore;

[TestFixture]
public sealed class TimeFlowServiceTests
{
    [Test]
    public void Constructor_RegistersDefaultHierarchy()
    {
        var service = new TimeFlowService();
        int simulationId = service.EnsureDomain(TimeFlowDomainIds.Simulation);
        int gasId = service.EnsureDomain(TimeFlowDomainIds.Gas, TimeFlowDomainIds.Simulation);
        int simulationIdAgain = service.EnsureDomain(TimeFlowDomainIds.Simulation);

        Assert.Multiple(() =>
        {
            Assert.That(simulationId, Is.GreaterThan(0));
            Assert.That(gasId, Is.GreaterThan(0));
            Assert.That(simulationIdAgain, Is.EqualTo(simulationId));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(TimeFlowService.DefaultScalePermille));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(TimeFlowService.DefaultScalePermille));
        });
    }

    [Test]
    public void ParentScaleAndPause_PropagateToChildDomains()
    {
        var service = new TimeFlowService();
        TimeFlowToken simulationScale = service.AcquireScaleToken(TimeFlowDomainIds.Simulation, 500, owner: "test", reason: "simulation scale token");
        TimeFlowToken gasScale = service.AcquireScaleToken(TimeFlowDomainIds.Gas, 2000, owner: "test", reason: "gas scale token");

        Assert.Multiple(() =>
        {
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(500));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(1000));
            Assert.That(service.GetScalePermilleRelativeToParent(TimeFlowDomainIds.Gas), Is.EqualTo(2000));
        });

        TimeFlowToken pause = service.AcquirePauseToken(TimeFlowDomainIds.Simulation, owner: "test", reason: "simulation pause token");
        Assert.Multiple(() =>
        {
            Assert.That(service.IsPaused(TimeFlowDomainIds.Simulation), Is.True);
            Assert.That(service.IsPaused(TimeFlowDomainIds.Gas), Is.True);
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(0));
            Assert.That(service.GetScalePermilleRelativeToParent(TimeFlowDomainIds.Gas), Is.EqualTo(0));
        });

        service.ReleaseToken(pause);
        service.ReleaseToken(simulationScale);

        Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(2000));
        Assert.That(service.GetScalePermilleRelativeToParent(TimeFlowDomainIds.Gas), Is.EqualTo(2000));

        service.ReleaseToken(gasScale);
        Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(1000));
        Assert.That(service.GetScalePermilleRelativeToParent(TimeFlowDomainIds.Gas), Is.EqualTo(1000));
    }

    [Test]
    public void PauseTokens_ReleaseIndependentlyForSameDomain()
    {
        var service = new TimeFlowService();
        TimeFlowToken skillIndicator = service.AcquirePauseToken(
            TimeFlowDomainIds.Simulation,
            owner: "skill-indicator",
            reason: "skill target indicator pause");
        TimeFlowToken menu = service.AcquirePauseToken(
            TimeFlowDomainIds.Simulation,
            owner: "menu",
            reason: "interface menu pause");

        Assert.Multiple(() =>
        {
            Assert.That(service.IsPaused(TimeFlowDomainIds.Simulation), Is.True);
            Assert.That(service.CaptureSnapshot().ActiveTokens, Has.Count.EqualTo(2));
        });

        service.ReleaseToken(menu);
        TimeFlowSnapshot skillOnly = service.CaptureSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(service.IsPaused(TimeFlowDomainIds.Simulation), Is.True);
            Assert.That(skillOnly.ActiveTokens, Has.Count.EqualTo(1));
            Assert.That(skillOnly.ActiveTokens[0].Reason, Is.EqualTo("skill target indicator pause"));
        });

        service.ReleaseToken(skillIndicator);
        Assert.That(service.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
    }

    [Test]
    public void TokenRequests_FailFastForInvalidOwnershipAndRelease()
    {
        var service = new TimeFlowService();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => service.AcquirePauseToken(TimeFlowDomainIds.Simulation, owner: ""));
            Assert.Throws<ArgumentException>(() => service.AcquireScaleToken(TimeFlowDomainIds.Simulation, 500, owner: ""));
            Assert.Throws<InvalidOperationException>(() => service.AcquireScaleToken(TimeFlowDomainIds.Simulation, 0, owner: "test"));
            Assert.Throws<InvalidOperationException>(() => service.AcquireScaleToken(TimeFlowDomainIds.Simulation, -1, owner: "test"));
            Assert.Throws<InvalidOperationException>(() => service.AcquireScaleToken(TimeFlowDomainIds.Simulation, TimeFlowService.MaxScalePermille + 1, owner: "test"));
            Assert.Throws<InvalidOperationException>(() => service.ReleaseToken(TimeFlowToken.Invalid));
        });

        TimeFlowToken token = service.AcquirePauseToken(TimeFlowDomainIds.Simulation, owner: "test");
        service.ReleaseToken(token);

        Assert.Throws<InvalidOperationException>(() => service.ReleaseToken(token));
    }

    [Test]
    public void Domains_FailFastForUnregisteredAndConflictingDefinitions()
    {
        var service = new TimeFlowService();

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => service.GetEffectiveScalePermille("simulation.missing"));
            Assert.Throws<InvalidOperationException>(() => service.IsPaused("simulation.missing"));
            Assert.Throws<InvalidOperationException>(() => service.AcquirePauseToken("simulation.missing", owner: "test"));
            Assert.Throws<InvalidOperationException>(() => service.AcquireScaleToken("simulation.missing", 1000, owner: "test"));
            Assert.Throws<InvalidOperationException>(() => service.EnsureDomain("simulation.projectiles", "simulation.missing"));
            Assert.Throws<InvalidOperationException>(() => service.EnsureDomain("simulation.invalid", TimeFlowDomainIds.Simulation, 0));
            Assert.Throws<InvalidOperationException>(() => service.EnsureDomain("simulation.invalid", TimeFlowDomainIds.Simulation, TimeFlowService.MaxScalePermille + 1));
        });

        int domainId = service.EnsureDomain("simulation.projectiles", TimeFlowDomainIds.Simulation, 1500);
        Assert.That(domainId, Is.GreaterThan(0));

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => service.EnsureDomain("simulation.projectiles", TimeFlowDomainIds.Simulation, 1000));
            Assert.Throws<InvalidOperationException>(() => service.EnsureDomain("simulation.projectiles", TimeFlowDomainIds.Gas, 1500));
        });
    }
}
