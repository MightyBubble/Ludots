namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Defines how an effect's stack count contributes to a tag count.
    /// </summary>
    public enum TagContributionFormula : byte
    {
        /// <summary>Tag count = Amount (fixed, independent of stack count).</summary>
        Fixed = 0,
        /// <summary>Tag count = StackCount * Amount.</summary>
        Linear = 1,
        /// <summary>Tag count = Base + StackCount * Amount.</summary>
        LinearPlusBase = 2,
        /// <summary>Tag count is computed by a graph program.</summary>
        GraphProgram = 255,
    }

    /// <summary>
    /// A single tag contribution declaration: "this effect contributes N tags of type TagId".
    /// </summary>
    public struct TagContribution
    {
        public int TagId;
        public TagContributionFormula Formula;
        /// <summary>Coefficient for Fixed/Linear, slope for LinearPlusBase.</summary>
        public ushort Amount;
        /// <summary>Base value for LinearPlusBase formula.</summary>
        public ushort Base;
        /// <summary>Graph program ID when Formula == GraphProgram.</summary>
        public int GraphProgramId;

        /// <summary>
        /// Compute the tag count contribution for a given stack count.
        /// For GraphProgram, returns 0 (caller must evaluate the graph separately).
        /// </summary>
        public int Compute(int stackCount)
        {
            return Formula switch
            {
                TagContributionFormula.Fixed => Amount,
                TagContributionFormula.Linear => stackCount * Amount,
                TagContributionFormula.LinearPlusBase => Base + stackCount * Amount,
                _ => 0, // GraphProgram: handled externally
            };
        }
    }

    /// <summary>
    /// ECS component: declares which tags a duration effect grants to its target.
    /// Attached to effect entities that contribute tag counts.
    /// 0GC inline storage, max 8 entries.
    /// </summary>
    public unsafe struct EffectGrantedTags
    {
        public const int MAX_GRANTS = GasConstants.EFFECT_GRANTED_TAGS_MAX;

        // Inline arrays for 0GC. Each index corresponds to one TagContribution.
        public fixed int TagIds[MAX_GRANTS];
        public fixed byte Formulas[MAX_GRANTS];
        public fixed ushort Amounts[MAX_GRANTS];
        public fixed ushort Bases[MAX_GRANTS];
        public fixed int GraphProgramIds[MAX_GRANTS];
        public int Count;

        public bool Add(in TagContribution contribution)
        {
            if (Count >= MAX_GRANTS) return false;
            TagIds[Count] = contribution.TagId;
            Formulas[Count] = (byte)contribution.Formula;
            Amounts[Count] = contribution.Amount;
            Bases[Count] = contribution.Base;
            GraphProgramIds[Count] = contribution.GraphProgramId;
            Count++;
            return true;
        }

        public TagContribution Get(int index)
        {
            if (index < 0 || index >= Count) return default;
            return new TagContribution
            {
                TagId = TagIds[index],
                Formula = (TagContributionFormula)Formulas[index],
                Amount = Amounts[index],
                Base = Bases[index],
                GraphProgramId = GraphProgramIds[index],
            };
        }
    }
}
