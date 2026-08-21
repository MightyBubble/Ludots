namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Optional filters authored on a TriggerGraph entry. All fields are optional;
    /// a dispatched event matches only when every declared filter matches its payload.
    /// Payload matching lives in Core (TriggerGraphEntryFiltersEvaluator) because it reads
    /// ScriptContext payloads.
    /// </summary>
    public readonly struct TriggerGraphEntryFilters
    {
        public TriggerGraphEntryFilters(
            string? region,
            string? tag,
            int? team,
            float? threshold,
            TriggerGraphEntryFilterDirection? direction)
        {
            Region = region;
            Tag = tag;
            Team = team;
            Threshold = threshold;
            Direction = direction;
        }

        public string? Region { get; }
        public string? Tag { get; }
        public int? Team { get; }
        public float? Threshold { get; }
        public TriggerGraphEntryFilterDirection? Direction { get; }

        public bool IsEmpty =>
            Region == null &&
            Tag == null &&
            !Team.HasValue &&
            !Threshold.HasValue &&
            !Direction.HasValue;
    }

    /// <summary>
    /// Threshold comparison semantics for the threshold/direction filter pair,
    /// evaluated against the Count payload of count-type events.
    /// </summary>
    public enum TriggerGraphEntryFilterDirection
    {
        CrossAbove = 1,
        CrossBelow = 2,
    }
}
