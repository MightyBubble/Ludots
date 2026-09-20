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
            TriggerGraphEntryFilterDirection? direction,
            string? action = null,
            string? instanceId = null,
            int? tagId = null,
            string? varName = null)
        {
            Region = region;
            Tag = tag;
            Team = team;
            Threshold = threshold;
            Direction = direction;
            Action = action;
            InstanceId = instanceId;
            TagId = tagId;
            VarName = varName;
        }

        public string? Region { get; }
        public string? Tag { get; }
        public int? Team { get; }
        public float? Threshold { get; }
        public TriggerGraphEntryFilterDirection? Direction { get; }
        public string? Action { get; }
        /// <summary>Exact placed-instance subscription ("this very unit"); matched by
        /// reverse-resolving the event's SourceEntity through MapLoadEntityIndex.</summary>
        public string? InstanceId { get; }
        /// <summary>Tag id resolved from <see cref="Tag"/> at mount time (GraphRuntime may not
        /// reference the GAS registry); unset while Tag is declared means "never matches".</summary>
        public int? TagId { get; }
        /// <summary>Exact map-variable subscription for MapVariableChanged ("this very
        /// variable"); matched against the event's VarName payload.</summary>
        public string? VarName { get; }

        public bool IsEmpty =>
            Region == null &&
            Tag == null &&
            !Team.HasValue &&
            !Threshold.HasValue &&
            !Direction.HasValue &&
            Action == null &&
            InstanceId == null &&
            VarName == null;
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
