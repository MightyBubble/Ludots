using Ludots.Core.Networking.Commands;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkGameplayCommandGateTests
{
    [Test]
    public void Gate_ReportsTypedRejectionsBeforeStartAndAfterCompletion()
    {
        var gate = new NetworkGameplayCommandGate();

        Assert.That(gate.TryAdmit(out NetworkCommandAdmissionCode waiting), Is.False);
        Assert.That(waiting, Is.EqualTo(NetworkCommandAdmissionCode.NetworkMatchNotStarted));

        gate.StartMatch();
        Assert.That(gate.TryAdmit(out _), Is.True);

        gate.CompleteMatch();
        Assert.That(gate.TryAdmit(out NetworkCommandAdmissionCode completed), Is.False);
        Assert.That(completed, Is.EqualTo(NetworkCommandAdmissionCode.NetworkMatchCompleted));

        gate.StartMatch();
        Assert.That(gate.Phase, Is.EqualTo(NetworkGameplayCommandPhase.Completed));
    }

    [Test]
    public void Gate_RejectsCompletionBeforeStart()
    {
        var gate = new NetworkGameplayCommandGate();
        Assert.Throws<InvalidOperationException>(gate.CompleteMatch);
    }
}
