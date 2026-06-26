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
        int physicsId = service.EnsureDomain(TimeFlowDomainIds.Physics2D, TimeFlowDomainIds.Simulation);
        int simulationIdAgain = service.EnsureDomain(TimeFlowDomainIds.Simulation, baseScalePermille: 500);

        Assert.Multiple(() =>
        {
            Assert.That(simulationId, Is.GreaterThan(0));
            Assert.That(gasId, Is.GreaterThan(0));
            Assert.That(physicsId, Is.GreaterThan(0));
            Assert.That(simulationIdAgain, Is.EqualTo(simulationId));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(TimeFlowService.DefaultScalePermille));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(TimeFlowService.DefaultScalePermille));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Physics2D), Is.EqualTo(TimeFlowService.DefaultScalePermille));
        });
    }

    [Test]
    public void ParentScaleAndPause_PropagateToChildDomains()
    {
        var service = new TimeFlowService();
        TimeFlowToken simulationScale = service.AcquireScaleToken(TimeFlowDomainIds.Simulation, 500, owner: "test", reason: "slow world");
        TimeFlowToken gasScale = service.AcquireScaleToken(TimeFlowDomainIds.Gas, 2000, owner: "test", reason: "keep gas realtime");

        Assert.Multiple(() =>
        {
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(500));
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(1000));
        });

        TimeFlowToken pause = service.AcquirePauseToken(TimeFlowDomainIds.Simulation, owner: "test", reason: "command pause");
        Assert.Multiple(() =>
        {
            Assert.That(service.IsPaused(TimeFlowDomainIds.Simulation), Is.True);
            Assert.That(service.IsPaused(TimeFlowDomainIds.Gas), Is.True);
            Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(0));
        });

        service.ReleaseToken(pause);
        service.ReleaseToken(simulationScale);

        Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(2000));

        service.ReleaseToken(gasScale);
        Assert.That(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(1000));
    }
}
