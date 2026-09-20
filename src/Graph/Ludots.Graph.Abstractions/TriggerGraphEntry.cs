namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Compiled TriggerGraph dispatch row. EventName is a plain EventKey string compared
    /// case-insensitively at dispatch time; StartPc is an absolute program counter.
    /// Action-bound entries (ActionId non-empty) listen to a semantic input action instead
    /// of a map event — EventName then names the shared InputAction payload schema used by
    /// CaptureEntryPayload, and the mount does not join the event bus.
    /// </summary>
    public readonly struct TriggerGraphEntry
    {
        public const string RefireIgnore = "ignore";
        public const string RefireRestart = "restart";

        /// <summary>Payload-schema event name synthesized for action-bound entries.</summary>
        public const string InputActionSchemaEventName = "InputAction";

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
            bool isHookFragment = false,
            string actionId = "")
        {
            Label = label;
            EventName = eventName;
            StartPc = startPc;
            Once = once;
            Filters = filters;
            Refire = refire;
            Priority = priority;
            IsHookFragment = isHookFragment;
            ActionId = actionId ?? string.Empty;
        }

        public string Label { get; }
        public string EventName { get; }
        public int StartPc { get; }
        public bool Once { get; }
        public TriggerGraphEntryFilters Filters { get; }

        /// <summary>Normalized refire policy: "ignore" (default) or "restart".</summary>
        public string Refire { get; }

        /// <summary>
        /// Dispatch order within one event key: ascending, negative runs earlier.
        /// Feeds Trigger.Priority; the runtime insertion already sorts.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Hook fragment entry: the body is woven into another graph's anchor at compile
        /// time; mounting creates no dispatch trigger for it.
        /// </summary>
        public bool IsHookFragment { get; }

        /// <summary>
        /// Semantic input action id when this entry binds an action directly; empty for
        /// ordinary event-listening entries.
        /// </summary>
        public string ActionId { get; }

        public bool IsActionBound => !string.IsNullOrWhiteSpace(ActionId);
    }
}
