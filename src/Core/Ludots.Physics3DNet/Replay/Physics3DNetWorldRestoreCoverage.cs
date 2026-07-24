using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Narrow formal restore contract. Full Bepu world restore is not implementable from public pose snapshots.
/// Networking must not claim world rollback and must not invent a fallback.
/// </summary>
public interface IPhysics3DNetWorldRestorePort
{
    bool IsSupported { get; }

    /// <summary>
    /// Required coverage for a future formal restore port:
    /// bodies, stable slots, constraint accumulated impulses, contact manifold cache,
    /// sleep islands, broadphase proxies, and command/input cursors.
    /// </summary>
    Physics3DNetWorldRestoreCoverageReport Coverage { get; }

    void RestoreExactWorldState(long snapshotTick);
}

public readonly struct Physics3DNetWorldRestoreCoverageItem
{
    public Physics3DNetWorldRestoreCoverageItem(string resource, bool supported, string reason)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource is required.", nameof(resource));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        Resource = resource;
        Supported = supported;
        Reason = reason;
    }

    public string Resource { get; }
    public bool Supported { get; }
    public string Reason { get; }
}

public sealed class Physics3DNetWorldRestoreCoverageReport
{
    private readonly Physics3DNetWorldRestoreCoverageItem[] _items;

    public Physics3DNetWorldRestoreCoverageReport(Physics3DNetWorldRestoreCoverageItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length == 0)
        {
            throw new ArgumentException("Coverage report must list required resources.", nameof(items));
        }

        // Own a copy so callers cannot mutate the report after construction.
        _items = new Physics3DNetWorldRestoreCoverageItem[items.Length];
        Array.Copy(items, _items, items.Length);

        bool anyUnsupported = false;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!_items[i].Supported)
            {
                anyUnsupported = true;
                break;
            }
        }

        AllSupported = !anyUnsupported;
    }

    public bool AllSupported { get; }
    public ReadOnlySpan<Physics3DNetWorldRestoreCoverageItem> Items => _items;
}

public sealed class Physics3DNetWorldRestoreUnsupportedException : NotSupportedException
{
    public Physics3DNetWorldRestoreUnsupportedException(Physics3DNetWorldRestoreCoverageReport coverage, long snapshotTick)
        : base(
            $"Full Physics3D world restore is unsupported at snapshot tick {snapshotTick}. "
            + "Pose snapshots cannot reconstruct Bepu constraint impulses, contact manifolds, sleep islands, or broadphase proxies.")
    {
        Coverage = coverage;
        SnapshotTick = snapshotTick;
    }

    public Physics3DNetWorldRestoreCoverageReport Coverage { get; }
    public long SnapshotTick { get; }
}

/// <summary>
/// Explicit unsupported restore port. Completes the vertical-slice honesty requirement without a fake fallback.
/// </summary>
public sealed class Physics3DNetUnsupportedWorldRestorePort : IPhysics3DNetWorldRestorePort
{
    public Physics3DNetUnsupportedWorldRestorePort()
    {
        Coverage = CreateUnsupportedCoverageReport();
    }

    public bool IsSupported => false;
    public Physics3DNetWorldRestoreCoverageReport Coverage { get; }

    public void RestoreExactWorldState(long snapshotTick)
    {
        if (snapshotTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotTick));
        }

        throw new Physics3DNetWorldRestoreUnsupportedException(Coverage, snapshotTick);
    }

    public static Physics3DNetWorldRestoreCoverageReport CreateUnsupportedCoverageReport()
    {
        const string reason =
            "Not exposed by Ludots.Physics3D public API; cannot be reconstructed from pose snapshots alone.";
        return new Physics3DNetWorldRestoreCoverageReport(
        [
            new Physics3DNetWorldRestoreCoverageItem("bodies", supported: false, reason),
            new Physics3DNetWorldRestoreCoverageItem("stable slots", supported: false, reason),
            new Physics3DNetWorldRestoreCoverageItem("constraint accumulated impulses", supported: false, reason),
            new Physics3DNetWorldRestoreCoverageItem("contact manifold cache", supported: false, reason),
            new Physics3DNetWorldRestoreCoverageItem("sleep islands", supported: false, reason),
            new Physics3DNetWorldRestoreCoverageItem("broadphase proxies", supported: false, reason),
            new Physics3DNetWorldRestoreCoverageItem("command/input cursors", supported: false, reason)
        ]);
    }
}
