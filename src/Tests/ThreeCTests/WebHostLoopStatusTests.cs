using System;
using Ludots.Adapter.Web;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebHostLoopStatusTests
    {
        [Test]
        public void CaptureHealthSnapshot_WhenRunningWithoutFault_IsOk()
        {
            var status = new WebHostLoopStatus();
            status.MarkStarted();

            WebHostLoopHealthSnapshot snapshot = status.CaptureHealthSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Status, Is.EqualTo(WebHostLoopStatus.RunningStatus));
                Assert.That(snapshot.Healthy, Is.True);
                Assert.That(snapshot.Running, Is.True);
                Assert.That(snapshot.Faulted, Is.False);
                Assert.That(snapshot.FaultType, Is.Null);
                Assert.That(snapshot.FaultMessage, Is.Null);
            });
        }

        [Test]
        public void CaptureHealthSnapshot_WhenFaulted_IsUnhealthyAndExposesFault()
        {
            var status = new WebHostLoopStatus();
            status.MarkStarted();
            var fault = new InvalidOperationException("frame failed");

            status.MarkFaulted(fault);
            WebHostLoopHealthSnapshot snapshot = status.CaptureHealthSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Status, Is.EqualTo(WebHostLoopStatus.FaultedStatus));
                Assert.That(snapshot.Healthy, Is.False);
                Assert.That(snapshot.Running, Is.False);
                Assert.That(snapshot.Faulted, Is.True);
                Assert.That(snapshot.FaultType, Is.EqualTo(typeof(InvalidOperationException).FullName));
                Assert.That(snapshot.FaultMessage, Is.EqualTo("frame failed"));
            });
        }

        [Test]
        public void CaptureHealthSnapshot_WhenStoppedWithoutFault_IsUnhealthy()
        {
            var status = new WebHostLoopStatus();

            WebHostLoopHealthSnapshot snapshot = status.CaptureHealthSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Status, Is.EqualTo(WebHostLoopStatus.StoppedStatus));
                Assert.That(snapshot.Healthy, Is.False);
                Assert.That(snapshot.Running, Is.False);
                Assert.That(snapshot.Faulted, Is.False);
                Assert.That(snapshot.FaultType, Is.Null);
                Assert.That(snapshot.FaultMessage, Is.Null);
            });
        }
    }
}
