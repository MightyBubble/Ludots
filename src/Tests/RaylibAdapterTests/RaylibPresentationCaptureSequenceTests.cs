using Ludots.Adapter.Raylib;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibPresentationCaptureSequenceTests
{
    [Test]
    public void ExactCurrentMilestones_AreCapturedInConfiguredOrder()
    {
        var source = new TestMilestoneSource(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["setup"] = 0,
                ["formed"] = 1,
                ["battle"] = 2,
                ["result"] = 3,
            },
            new PresentationCaptureMilestoneSnapshot("setup", 0, 1));
        string target = Path.Combine(Path.GetTempPath(), "frontline.png");
        RaylibPresentationCaptureSequence sequence = RaylibPresentationCaptureSequence.Create(
            source,
            target,
            "formed,battle,result");

        Assert.That(sequence.TryPrepareCapture(10, out _), Is.False);

        source.Current = new PresentationCaptureMilestoneSnapshot("formed", 1, 2);
        Assert.That(sequence.TryPrepareCapture(11, out RaylibPresentationCaptureRequest formed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(formed.Milestone, Is.EqualTo("formed"));
            Assert.That(formed.MilestoneOrder, Is.EqualTo(1));
            Assert.That(formed.MilestoneRevision, Is.EqualTo(2));
            Assert.That(formed.HostFrame, Is.EqualTo(11));
            Assert.That(Path.GetFileName(formed.Path), Is.EqualTo("frontline_001_formed.png"));
        });
        sequence.CompleteCapture(in formed);

        source.Current = new PresentationCaptureMilestoneSnapshot("battle", 2, 3);
        Assert.That(sequence.TryPrepareCapture(27, out RaylibPresentationCaptureRequest battle), Is.True);
        Assert.That(Path.GetFileName(battle.Path), Is.EqualTo("frontline_002_battle.png"));
        sequence.CompleteCapture(in battle);

        source.Current = new PresentationCaptureMilestoneSnapshot("result", 3, 4);
        Assert.That(sequence.TryPrepareCapture(41, out RaylibPresentationCaptureRequest result), Is.True);
        Assert.That(Path.GetFileName(result.Path), Is.EqualTo("frontline_003_result.png"));
        sequence.CompleteCapture(in result);
        Assert.That(sequence.HasPending, Is.False);
    }

    [TestCase("formed,,battle", "non-empty ASCII identifier")]
    [TestCase("formed,battle,formed", "configured more than once")]
    [TestCase("battle,formed", "does not follow configured order")]
    [TestCase("formed,not-known", "unknown")]
    [TestCase("formed,bad milestone", "non-empty ASCII identifier")]
    public void InvalidConfiguration_FailsBeforeCapture(string configured, string expectedMessage)
    {
        TestMilestoneSource source = CreateSource();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RaylibPresentationCaptureSequence.Create(source, "capture.png", configured))!;

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [Test]
    public void MilestoneAndFrameModes_CannotBeCombined()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() =>
                RaylibPresentationCaptureSequence.ValidateCaptureMode("formed", "10", null));
            Assert.Throws<InvalidOperationException>(() =>
                RaylibPresentationCaptureSequence.ValidateCaptureMode("formed", null, "10,20"));
            Assert.That(
                RaylibPresentationCaptureSequence.ValidateCaptureMode("formed", null, null),
                Is.True);
            Assert.That(
                RaylibPresentationCaptureSequence.ValidateCaptureMode(null, "10", null),
                Is.False);
        });
    }

    [Test]
    public void SourceSkippingRequiredMilestone_FailsInsteadOfCapturingLaterState()
    {
        TestMilestoneSource source = CreateSource();
        RaylibPresentationCaptureSequence sequence = RaylibPresentationCaptureSequence.Create(
            source,
            "capture.png",
            "formed,battle");
        source.Current = new PresentationCaptureMilestoneSnapshot("battle", 2, 2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            sequence.TryPrepareCapture(15, out _))!;

        Assert.That(exception.Message, Does.Contain("before required milestone 'formed'"));
    }

    [Test]
    public void SourceChangingStateWithoutRevision_Fails()
    {
        TestMilestoneSource source = CreateSource();
        RaylibPresentationCaptureSequence sequence = RaylibPresentationCaptureSequence.Create(
            source,
            "capture.png",
            "formed");
        source.Current = new PresentationCaptureMilestoneSnapshot("formed", 1, 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            sequence.TryPrepareCapture(15, out _))!;

        Assert.That(exception.Message, Does.Contain("without advancing revision"));
    }

    [Test]
    public void SourceChangingMilestoneIdWithoutOrder_Fails()
    {
        var source = new TestMilestoneSource(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["setup"] = 0,
                ["setup-alias"] = 0,
                ["formed"] = 1,
            },
            new PresentationCaptureMilestoneSnapshot("setup", 0, 1));
        RaylibPresentationCaptureSequence sequence = RaylibPresentationCaptureSequence.Create(
            source,
            "capture.png",
            "formed");
        source.Current = new PresentationCaptureMilestoneSnapshot("setup-alias", 0, 2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            sequence.TryPrepareCapture(15, out _))!;

        Assert.That(exception.Message, Does.Contain("without advancing order"));
    }

    [Test]
    public void PreparedCapture_MustCompleteBeforeAnotherObservation()
    {
        TestMilestoneSource source = CreateSource();
        RaylibPresentationCaptureSequence sequence = RaylibPresentationCaptureSequence.Create(
            source,
            "capture.png",
            "formed");
        source.Current = new PresentationCaptureMilestoneSnapshot("formed", 1, 2);
        Assert.That(sequence.TryPrepareCapture(15, out _), Is.True);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            sequence.TryPrepareCapture(16, out _))!;

        Assert.That(exception.Message, Does.Contain("prepared but not completed"));
    }

    private static TestMilestoneSource CreateSource() => new(
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["setup"] = 0,
            ["formed"] = 1,
            ["battle"] = 2,
        },
        new PresentationCaptureMilestoneSnapshot("setup", 0, 1));

    private sealed class TestMilestoneSource : IPresentationCaptureMilestoneSource
    {
        private readonly IReadOnlyDictionary<string, int> _orders;

        public TestMilestoneSource(
            IReadOnlyDictionary<string, int> orders,
            PresentationCaptureMilestoneSnapshot current)
        {
            _orders = orders;
            Current = current;
        }

        public PresentationCaptureMilestoneSnapshot Current { get; set; }

        public bool TryResolveOrder(string milestoneId, out int order) =>
            _orders.TryGetValue(milestoneId, out order);
    }
}
