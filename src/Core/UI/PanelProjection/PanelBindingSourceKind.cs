namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// How a panel variable obtains its value. Projection over AttributeBuffer /
    /// GraphOutputValueStore only — never a parallel panel value store.
    /// </summary>
    public enum PanelBindingSourceKind : byte
    {
        /// <summary>Final value from an entity AttributeBuffer slot.</summary>
        SingleAttribute = 1,

        /// <summary>
        /// Derived attribute already written into AttributeBuffer by
        /// AttributeAggregatorSystem / AttributeDerivedGraphBinding.
        /// </summary>
        DerivedAttribute = 2,

        /// <summary>
        /// Cross-entity aggregate already projected into GraphOutputValueStore Summary.
        /// </summary>
        AggregateProjection = 3,

        /// <summary>
        /// Graph Summary output (same read mouth as AggregateProjection; authoring alias).
        /// </summary>
        GraphOutput = 4,

        /// <summary>
        /// Generic lookup table cell (#881): key read from an owner attribute,
        /// row resolved and field read through GraphLookupTableRegistry.
        /// </summary>
        TableLookup = 5,
    }
}
