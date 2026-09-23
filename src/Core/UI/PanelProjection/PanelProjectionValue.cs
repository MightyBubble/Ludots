using Arch.Core;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Value kinds a panel pin can carry, mirroring the graph output value kinds
    /// (Bool/Int/Float/Entity). The resolved value keeps the graph's actual kind —
    /// no numeric coercion at the projection boundary.
    /// </summary>
    public enum PanelValueKind : byte
    {
        Bool = 1,
        Int = 2,
        Float = 3,
        Entity = 4,
    }

    /// <summary>
    /// One resolved pin value. The value is always graph-sourced: missing graph
    /// output fails explicitly at read time (no silent default fallback), so a
    /// value without a materialized output never exists.
    /// </summary>
    public readonly struct PanelProjectionValue
    {
        public PanelProjectionValue(
            string pinName,
            PanelValueKind kind,
            uint revision,
            bool boolValue = false,
            int intValue = 0,
            float floatValue = 0f,
            Entity entityValue = default)
        {
            PinName = pinName;
            Kind = kind;
            Revision = revision;
            BoolValue = boolValue;
            IntValue = intValue;
            FloatValue = floatValue;
            EntityValue = entityValue;
        }

        public string PinName { get; }
        public PanelValueKind Kind { get; }

        /// <summary>Revision from the graph output store; the host uses it to detect value moves.</summary>
        public uint Revision { get; }

        public bool BoolValue { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public Entity EntityValue { get; }

        /// <summary>
        /// Numeric projection for display/consumers that render numbers: the raw
        /// Float value or the Int value widened to float. Bool/Entity have no numeric
        /// form and fail loudly instead of coercing.
        /// </summary>
        public float NumericValue => Kind switch
        {
            PanelValueKind.Float => FloatValue,
            PanelValueKind.Int => IntValue,
            _ => throw new System.InvalidOperationException(
                $"Panel pin '{PinName}' is a {Kind} value and has no numeric projection."),
        };
    }
}
