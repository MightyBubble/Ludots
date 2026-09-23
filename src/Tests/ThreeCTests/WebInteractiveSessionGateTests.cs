using System.Threading;
using System.Threading.Tasks;
using Ludots.Adapter.Web.Streaming;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebInteractiveSessionGateTests
    {
        [Test]
        public void Gate_AdmitsExactlyOneConcurrentInteractiveSession()
        {
            var gate = new WebInteractiveSessionGate();
            int admittedCount = 0;
            int admittedIndex = -1;

            Parallel.For(0, 64, index =>
            {
                if (gate.TryAcquire($"session-{index}"))
                {
                    Interlocked.Increment(ref admittedCount);
                    Interlocked.Exchange(ref admittedIndex, index);
                }
            });

            Assert.That(admittedCount, Is.EqualTo(1));
            Assert.That(admittedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(gate.Release($"session-{admittedIndex}"), Is.True);
            Assert.That(gate.TryAcquire("next-session"), Is.True);
        }

        [Test]
        public void Gate_NonOwnerCannotReleaseTheInteractiveSession()
        {
            var gate = new WebInteractiveSessionGate();

            Assert.That(gate.TryAcquire("owner"), Is.True);
            Assert.That(gate.Release("spectator"), Is.False);
            Assert.That(gate.TryAcquire("replacement"), Is.False);
            Assert.That(gate.Release("owner"), Is.True);
            Assert.That(gate.TryAcquire("replacement"), Is.True);
        }
    }
}
