using Ludots.Core.MassNavigation.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationTelemetryTests
    {
        [Test]
        public void BeginFrame_ResetsFrameCountersAndKeepsTotals()
        {
            var telemetry = new MassNavigationTelemetry();
            telemetry.MarkCommandSourceSnapshot();
            telemetry.MarkCommandApply();
            telemetry.MarkCommandRejected(12f, 34f);
            telemetry.MarkFocusBudgetUpdated();
            telemetry.MarkSolverWindowMoved();

            telemetry.BeginFrame(0.02f);

            Assert.That(telemetry.CommandSourceSnapshotCountFrame, Is.Zero);
            Assert.That(telemetry.CommandCountFrame, Is.Zero);
            Assert.That(telemetry.CommandRejectsFrame, Is.Zero);
            Assert.That(telemetry.FocusBudgetUpdatesFrame, Is.Zero);
            Assert.That(telemetry.SolverWindowMovesFrame, Is.Zero);
            Assert.That(telemetry.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(telemetry.FocusBudgetUpdatesTotal, Is.EqualTo(1));
            Assert.That(telemetry.SolverWindowMovesTotal, Is.EqualTo(1));
            Assert.That(telemetry.FrameMs, Is.EqualTo(20f).Within(0.001f));
            Assert.That(telemetry.Fps, Is.EqualTo(50f).Within(0.001f));
        }

        [Test]
        public void ObserveTiming_NegativeSample_ClampsToZero()
        {
            var telemetry = new MassNavigationTelemetry();

            telemetry.ObserveSimStep(-4d);

            Assert.That(telemetry.SimStepMs, Is.Zero);
        }

        [Test]
        public void ObservePerformerCoverage_NegativeCounts_ClampsToZero()
        {
            var telemetry = new MassNavigationTelemetry();

            telemetry.ObservePerformerCoverage(-1, -2, -3, -4);

            Assert.That(telemetry.CrowdInViewCount, Is.Zero);
            Assert.That(telemetry.CrowdSubmittedCount, Is.Zero);
            Assert.That(telemetry.ObstacleSubmittedCount, Is.Zero);
            Assert.That(telemetry.PerformerDroppedCount, Is.Zero);
        }
    }
}
