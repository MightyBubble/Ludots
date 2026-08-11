namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Resolved numeric panel projection value. Text/token semantics are a later slice;
    /// this mouth carries Float/Int/Bool already materialised by Attribute or GraphOutput.
    /// </summary>
    public readonly struct PanelProjectionValue
    {
        public PanelProjectionValue(string variableId, PanelBindingSourceKind sourceKind, float floatValue, uint revision)
        {
            VariableId = variableId;
            SourceKind = sourceKind;
            FloatValue = floatValue;
            Revision = revision;
        }

        public string VariableId { get; }
        public PanelBindingSourceKind SourceKind { get; }
        public float FloatValue { get; }
        public uint Revision { get; }
    }
}
