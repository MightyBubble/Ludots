namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Binds an entity to one or more Graph programs that compute derived attributes.
    /// Executed by <see cref="Systems.AttributeAggregatorSystem"/> after modifier aggregation
    /// and before dirty-flag marking, enabling non-linear attribute formulas
    /// (e.g., Ability Haste → CD Multiplier, Armor → Physical EHP).
    /// </summary>
    public unsafe struct AttributeDerivedGraphBinding
    {
        public const int MAX_BINDINGS = 8;
        public fixed int GraphProgramIds[MAX_BINDINGS];
        public int Count;

        public void Add(int graphProgramId)
        {
            if (Count >= MAX_BINDINGS) return;
            GraphProgramIds[Count] = graphProgramId;
            Count++;
        }
    }
}
