namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Compiled TriggerGraph dispatch row. EventName is a plain EventKey string compared
    /// case-insensitively at dispatch time; StartPc is an absolute program counter.
    /// </summary>
    public readonly struct TriggerGraphEntry
    {
        public const string RefireIgnore = "ignore";
        public const string RefireRestart = "restart";

        public TriggerGraphEntry(string label, string eventName, int startPc, bool once)
            : this(label, eventName, startPc, once, default, RefireIgnore)
        {
        }

        public TriggerGraphEntry(
            string label,
            string eventName,
            int startPc,
            bool once,
            TriggerGraphEntryFilters filters)
            : this(label, eventName, startPc, once, filters, RefireIgnore)
        {
        }

        public TriggerGraphEntry(
            string label,
            string eventName,
            int startPc,
            bool once,
            TriggerGraphEntryFilters filters,
            string refire)
        {
            Label = label;
            EventName = eventName;
            StartPc = startPc;
            Once = once;
            Filters = filters;
            Refire = refire;
        }

        public string Label { get; }
        public string EventName { get; }
        public int StartPc { get; }
        public bool Once { get; }
        public TriggerGraphEntryFilters Filters { get; }

        /// <summary>Normalized refire policy: "ignore" (default) or "restart".</summary>
        public string Refire { get; }
    }
}
