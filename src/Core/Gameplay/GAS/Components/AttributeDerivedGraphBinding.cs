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
            if (graphProgramId <= 0)
            {
                throw new System.InvalidOperationException("AttributeDerivedGraphBinding graphProgramId must be > 0.");
            }

            if (Count >= MAX_BINDINGS)
            {
                throw new System.InvalidOperationException(
                    $"AttributeDerivedGraphBinding supports at most {MAX_BINDINGS} graph bindings.");
            }

            GraphProgramIds[Count] = graphProgramId;
            Count++;
        }
    }
}
