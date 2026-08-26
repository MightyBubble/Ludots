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
            string refire,
            int priority = 0,
            bool isHookFragment = false)
        {
            Label = label;
            EventName = eventName;
            StartPc = startPc;
            Once = once;
            Filters = filters;
            Refire = refire;
            Priority = priority;
            IsHookFragment = isHookFragment;
        }

        public string Label { get; }
        public string EventName { get; }
        public int StartPc { get; }
        public bool Once { get; }
        public TriggerGraphEntryFilters Filters { get; }

        /// <summary>Normalized refire policy: "ignore" (default) or "restart".</summary>
        public string Refire { get; }

        /// <summary>
        /// Dispatch order within one event key (#1124): ascending, negative runs
        /// earlier. Feeds Trigger.Priority; the runtime insertion already sorts.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Hook fragment entry (#1124): the body is woven into another graph's anchor
        /// at compile time; mounting creates no dispatch trigger for it.
        /// </summary>
        public bool IsHookFragment { get; }
    }
}
