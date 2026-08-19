namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Compiled MapTrigger dispatch row. EventName is a plain EventKey string compared
    /// case-insensitively at dispatch time; StartPc is an absolute program counter.
    /// </summary>
    public readonly struct MapTriggerGraphEntry
    {
        public const string RefireIgnore = "ignore";
        public const string RefireRestart = "restart";

        public MapTriggerGraphEntry(string label, string eventName, int startPc, bool once)
            : this(label, eventName, startPc, once, default, RefireIgnore)
        {
        }

        public MapTriggerGraphEntry(
            string label,
            string eventName,
            int startPc,
            bool once,
            MapTriggerEntryFilters filters)
            : this(label, eventName, startPc, once, filters, RefireIgnore)
        {
        }

        public MapTriggerGraphEntry(
            string label,
            string eventName,
            int startPc,
            bool once,
            MapTriggerEntryFilters filters,
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
        public MapTriggerEntryFilters Filters { get; }

        /// <summary>Normalized refire policy: "ignore" (default) or "restart".</summary>
        public string Refire { get; }
    }
}
