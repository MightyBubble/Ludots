namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Compiled MapTrigger dispatch row. EventName is a plain EventKey string compared
    /// case-insensitively at dispatch time; StartPc is an absolute program counter.
    /// </summary>
    public readonly struct MapTriggerGraphEntry
    {
        public MapTriggerGraphEntry(string label, string eventName, int startPc, bool once)
        {
            Label = label;
            EventName = eventName;
            StartPc = startPc;
            Once = once;
        }

        public string Label { get; }
        public string EventName { get; }
        public int StartPc { get; }
        public bool Once { get; }
    }
}
