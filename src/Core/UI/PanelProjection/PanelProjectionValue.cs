namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// One resolved pin value: graph output when present, else the pin's declared
    /// default. Revision comes from the graph output store; defaults carry revision 0.
    /// </summary>
    public readonly struct PanelProjectionValue
    {
        public PanelProjectionValue(string pinName, float floatValue, uint revision, bool fromGraph)
        {
            PinName = pinName;
            FloatValue = floatValue;
            Revision = revision;
            FromGraph = fromGraph;
        }

        public string PinName { get; }
        public float FloatValue { get; }
        public uint Revision { get; }

        /// <summary>False = the graph has not produced this output yet; the pin default is showing.</summary>
        public bool FromGraph { get; }
    }
}
