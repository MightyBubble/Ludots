using System.Collections.Generic;

namespace DualSeatPanelsShowcaseMod.Runtime;

/// <summary>
/// One seat-attributed panel operation outcome, as returned by
/// <c>PanelEventDispatcher.FireFromSeat</c>: admitted operations land in the sink;
/// refused ones carry the engine's reason verbatim for the HUD to show.
/// </summary>
public sealed record DualSeatPanelOutcome(
    string SeatId,
    string PanelId,
    string EventId,
    bool Admitted,
    string? Reason);

/// <summary>
/// Ring of recent per-seat outcomes shared between the input system (writer) and the
/// HUD overlay (reader). This is UI feedback state only — panel data keeps flowing
/// through the panel host, gameplay through the event/graph pipeline.
/// </summary>
public sealed class DualSeatPanelsFeedback
{
    private readonly object _gate = new();
    private readonly Queue<DualSeatPanelOutcome> _recent = new();

    public void Record(DualSeatPanelOutcome outcome)
    {
        lock (_gate)
        {
            _recent.Enqueue(outcome);
            while (_recent.Count > 6)
            {
                _recent.Dequeue();
            }
        }
    }

    public List<DualSeatPanelOutcome> Snapshot()
    {
        lock (_gate)
        {
            return new List<DualSeatPanelOutcome>(_recent);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _recent.Clear();
        }
    }
}
