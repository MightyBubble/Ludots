namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// One resolved pin value. Numeric Graph values retain the legacy float contract;
    /// structured data values are carried as a JsonNode for typed skin consumers.
    /// </summary>
    public readonly struct PanelProjectionValue
    {
        public PanelProjectionValue(string pinName, float floatValue, uint revision, bool fromGraph)
        {
            PinName = pinName;
            FloatValue = floatValue;
            Node = null;
            Revision = revision;
            FromGraph = fromGraph;
            FromData = false;
        }

        public PanelProjectionValue(string pinName, System.Text.Json.Nodes.JsonNode node, uint revision)
        {
            PinName = pinName;
            Node = node ?? throw new System.ArgumentNullException(nameof(node));
            FloatValue = TryReadFloat(node, out float value) ? value : 0f;
            Revision = revision;
            FromGraph = false;
            FromData = true;
        }

        public string PinName { get; }
        public float FloatValue { get; }
        public System.Text.Json.Nodes.JsonNode? Node { get; }
        public uint Revision { get; }

        /// <summary>False = the graph has not produced this output yet; the pin default is showing.</summary>
        public bool FromGraph { get; }

        public bool FromData { get; }

        private static bool TryReadFloat(System.Text.Json.Nodes.JsonNode node, out float value)
        {
            if (node is System.Text.Json.Nodes.JsonValue jsonValue &&
                jsonValue.TryGetValue<double>(out double raw) &&
                raw >= float.MinValue && raw <= float.MaxValue)
            {
                value = (float)raw;
                return true;
            }

            value = 0f;
            return false;
        }
    }
}
