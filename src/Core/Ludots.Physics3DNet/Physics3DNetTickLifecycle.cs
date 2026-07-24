using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Sole authoritative tick truth for Physics3DNet: ExecutingTick → CommittedTick → SnapshotTick.
/// Input rings and other observers must read these properties; they must never copy or advance them.
/// Network observers may only publish from a committed tick.
/// </summary>
public sealed class Physics3DNetTickLifecycle
{
    private readonly int _snapshotIntervalTicks;

    public Physics3DNetTickLifecycle(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _snapshotIntervalTicks = config.AuthoritativeHz / config.SnapshotHz;
        AuthoritativeHz = config.AuthoritativeHz;
        SnapshotHz = config.SnapshotHz;
    }

    public int AuthoritativeHz { get; }
    public int SnapshotHz { get; }
    public int SnapshotIntervalTicks => _snapshotIntervalTicks;

    /// <summary>Tick currently executing. Zero means idle (nothing executing).</summary>
    public long ExecutingTick { get; private set; }

    /// <summary>Last fully committed authoritative tick. Zero means none yet.</summary>
    public long CommittedTick { get; private set; }

    /// <summary>Last published snapshot tick. Zero means none yet. Never advances past CommittedTick.</summary>
    public long SnapshotTick { get; private set; }

    public bool IsExecuting => ExecutingTick > 0;

    public void BeginExecute(long tick)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick must be positive.");
        }

        if (IsExecuting)
        {
            throw new InvalidOperationException(
                $"Cannot begin ExecutingTick {tick} while ExecutingTick {ExecutingTick} is still open.");
        }

        if (tick != CommittedTick + 1)
        {
            throw new InvalidOperationException(
                $"ExecutingTick must advance monotonically from CommittedTick+1. Expected {CommittedTick + 1}, got {tick}.");
        }

        ExecutingTick = tick;
    }

    public void Commit()
    {
        if (!IsExecuting)
        {
            throw new InvalidOperationException("Cannot commit when no ExecutingTick is open.");
        }

        CommittedTick = ExecutingTick;
        ExecutingTick = 0;
    }

    public bool IsSnapshotBoundary(long tick)
    {
        if (tick <= 0)
        {
            return false;
        }

        return tick % _snapshotIntervalTicks == 0;
    }

    /// <summary>
    /// Publishes SnapshotTick. Requires a committed boundary tick; never publishes ExecutingTick.
    /// </summary>
    public void PublishSnapshot(long tick)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick must be positive.");
        }

        if (IsExecuting)
        {
            throw new InvalidOperationException(
                $"Cannot publish SnapshotTick {tick} while ExecutingTick {ExecutingTick} is open.");
        }

        if (tick > CommittedTick)
        {
            throw new InvalidOperationException(
                $"SnapshotTick {tick} cannot exceed CommittedTick {CommittedTick}.");
        }

        if (tick <= SnapshotTick)
        {
            throw new InvalidOperationException(
                $"SnapshotTick must advance monotonically. Current {SnapshotTick}, requested {tick}.");
        }

        if (!IsSnapshotBoundary(tick))
        {
            throw new InvalidOperationException(
                $"Tick {tick} is not a snapshot boundary for interval {_snapshotIntervalTicks}.");
        }

        SnapshotTick = tick;
    }
}
