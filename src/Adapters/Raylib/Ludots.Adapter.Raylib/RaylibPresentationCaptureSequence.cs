using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Raylib;

internal readonly record struct RaylibPresentationCaptureRequest(
    string Path,
    string Milestone,
    int MilestoneOrder,
    uint MilestoneRevision,
    int HostFrame,
    int SequenceIndex);

internal sealed class RaylibPresentationCaptureSequence
{
    private readonly IPresentationCaptureMilestoneSource _source;
    private readonly string _targetPath;
    private readonly string[] _milestones;
    private readonly int[] _orders;
    private PresentationCaptureMilestoneSnapshot _lastSnapshot;
    private RaylibPresentationCaptureRequest _preparedCapture;
    private int _nextIndex;
    private bool _hasPreparedCapture;

    private RaylibPresentationCaptureSequence(
        IPresentationCaptureMilestoneSource source,
        string targetPath,
        string[] milestones,
        int[] orders)
    {
        _source = source;
        _targetPath = Path.GetFullPath(targetPath);
        _milestones = milestones;
        _orders = orders;
        _lastSnapshot = ValidateSnapshot(source, source.Current);
    }

    public bool HasPending => _nextIndex < _milestones.Length;

    public static bool ValidateCaptureMode(
        string? rawMilestones,
        string? rawFrame,
        string? rawFrames)
    {
        bool milestoneMode = rawMilestones != null;
        if (milestoneMode && (rawFrame != null || rawFrames != null))
        {
            throw new InvalidOperationException(
                "LUDOTS_TAKE_SCREENSHOT_MILESTONES cannot be combined with frame-based screenshot capture.");
        }

        return milestoneMode;
    }

    public static RaylibPresentationCaptureSequence Create(
        IPresentationCaptureMilestoneSource source,
        string targetPath,
        string rawMilestones)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException(
                "LUDOTS_TAKE_SCREENSHOT_PATH is required when milestone screenshot capture is configured.");
        }
        if (rawMilestones == null)
        {
            throw new ArgumentNullException(nameof(rawMilestones));
        }

        string[] parts = rawMilestones.Split(',', StringSplitOptions.None);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("Presentation capture milestones cannot be empty.");
        }

        var milestones = new string[parts.Length];
        var orders = new int[parts.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int previousOrder = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            string milestone = parts[i].Trim();
            if (!IsValidMilestoneId(milestone))
            {
                throw new InvalidOperationException(
                    $"Presentation capture milestone at position {i + 1} must be a non-empty ASCII identifier using only letters, digits, '.', '_' or '-'.");
            }
            if (!seen.Add(milestone))
            {
                throw new InvalidOperationException(
                    $"Presentation capture milestone '{milestone}' is configured more than once.");
            }
            if (!source.TryResolveOrder(milestone, out int order))
            {
                throw new InvalidOperationException(
                    $"Presentation capture milestone '{milestone}' is unknown to source '{source.GetType().FullName}'.");
            }
            if (order < 0)
            {
                throw new InvalidOperationException(
                    $"Presentation capture milestone '{milestone}' resolved to invalid negative order {order}.");
            }
            if (order <= previousOrder)
            {
                throw new InvalidOperationException(
                    $"Presentation capture milestone '{milestone}' has order {order}, which does not follow configured order {previousOrder}.");
            }
            if (i > 0 && order != previousOrder + 1)
            {
                throw new InvalidOperationException(
                    $"Presentation capture milestone '{milestone}' has order {order}; configured milestones must immediately follow order {previousOrder}.");
            }

            milestones[i] = milestone;
            orders[i] = order;
            previousOrder = order;
        }

        return new RaylibPresentationCaptureSequence(source, targetPath, milestones, orders);
    }

    public bool TryPrepareCapture(int hostFrame, out RaylibPresentationCaptureRequest request)
    {
        if (_hasPreparedCapture)
        {
            throw new InvalidOperationException(
                $"Presentation capture milestone '{_preparedCapture.Milestone}' was prepared but not completed.");
        }

        PresentationCaptureMilestoneSnapshot current = ValidateSnapshot(_source, _source.Current);
        ValidateMonotonicSnapshot(_lastSnapshot, current);
        _lastSnapshot = current;

        if (!HasPending)
        {
            request = default;
            return false;
        }

        string expectedMilestone = _milestones[_nextIndex];
        int expectedOrder = _orders[_nextIndex];
        if (current.Order < expectedOrder)
        {
            request = default;
            return false;
        }
        if (current.Order > expectedOrder)
        {
            throw new InvalidOperationException(
                $"Presentation capture source advanced to milestone '{current.Id}' order {current.Order} revision {current.Revision} " +
                $"before required milestone '{expectedMilestone}' order {expectedOrder} was captured.");
        }
        if (!string.Equals(current.Id, expectedMilestone, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Presentation capture source reported milestone '{current.Id}' for order {current.Order}; " +
                $"required milestone is '{expectedMilestone}'.");
        }

        request = new RaylibPresentationCaptureRequest(
            BuildMilestoneScreenshotPath(_targetPath, _nextIndex, current.Id),
            current.Id,
            current.Order,
            current.Revision,
            hostFrame,
            _nextIndex);
        _preparedCapture = request;
        _hasPreparedCapture = true;
        return true;
    }

    public void CompleteCapture(in RaylibPresentationCaptureRequest request)
    {
        if (!_hasPreparedCapture || request != _preparedCapture)
        {
            throw new InvalidOperationException("Presentation capture completion does not match the prepared milestone request.");
        }

        _hasPreparedCapture = false;
        _nextIndex++;
    }

    internal static string BuildMilestoneScreenshotPath(string targetPath, int sequenceIndex, string milestone)
    {
        string fullTargetPath = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullTargetPath) ?? string.Empty;
        string extension = Path.GetExtension(fullTargetPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        string fileName = Path.GetFileNameWithoutExtension(fullTargetPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "screenshot";
        }

        string sequencedFileName = $"{fileName}_{sequenceIndex + 1:000}_{milestone}{extension}";
        return string.IsNullOrWhiteSpace(directory)
            ? Path.GetFullPath(sequencedFileName)
            : Path.Combine(directory, sequencedFileName);
    }

    private static PresentationCaptureMilestoneSnapshot ValidateSnapshot(
        IPresentationCaptureMilestoneSource source,
        PresentationCaptureMilestoneSnapshot snapshot)
    {
        if (!IsValidMilestoneId(snapshot.Id))
        {
            throw new InvalidOperationException(
                "Presentation capture source reported an invalid milestone identifier.");
        }
        if (snapshot.Revision == 0)
        {
            throw new InvalidOperationException(
                $"Presentation capture source reported milestone '{snapshot.Id}' with revision 0.");
        }
        if (!source.TryResolveOrder(snapshot.Id, out int resolvedOrder))
        {
            throw new InvalidOperationException(
                $"Presentation capture source reported unknown current milestone '{snapshot.Id}'.");
        }
        if (resolvedOrder != snapshot.Order)
        {
            throw new InvalidOperationException(
                $"Presentation capture source reported milestone '{snapshot.Id}' with order {snapshot.Order}, " +
                $"but resolves it to order {resolvedOrder}.");
        }
        if (snapshot.Order < 0)
        {
            throw new InvalidOperationException(
                $"Presentation capture source reported milestone '{snapshot.Id}' with invalid negative order {snapshot.Order}.");
        }

        return snapshot;
    }

    private static void ValidateMonotonicSnapshot(
        PresentationCaptureMilestoneSnapshot previous,
        PresentationCaptureMilestoneSnapshot current)
    {
        if (current.Revision < previous.Revision)
        {
            throw new InvalidOperationException(
                $"Presentation capture milestone revision moved backward from {previous.Revision} to {current.Revision}.");
        }
        if (current.Revision == previous.Revision && current != previous)
        {
            throw new InvalidOperationException(
                $"Presentation capture source changed milestone state without advancing revision {current.Revision}.");
        }
        if (current.Order < previous.Order)
        {
            throw new InvalidOperationException(
                $"Presentation capture milestone moved backward from '{previous.Id}' order {previous.Order} " +
                $"to '{current.Id}' order {current.Order}.");
        }
        if (current.Order == previous.Order &&
            !string.Equals(current.Id, previous.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Presentation capture source changed milestone id from '{previous.Id}' to '{current.Id}' " +
                $"without advancing order {current.Order}.");
        }
    }

    private static bool IsValidMilestoneId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            bool valid = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
