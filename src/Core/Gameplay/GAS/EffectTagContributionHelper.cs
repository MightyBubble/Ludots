using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Static helper for managing tag count contributions from effects.
    /// Called at Grant (OnApply), Revoke (OnExpire/OnRemove), and Update (stack change) points.
    /// 0GC: no allocations.
    /// </summary>
    public static class EffectTagContributionHelper
    {
        /// <summary>
        /// Grant tags to the target's <see cref="TagCountContainer"/> based on effect's granted tag declarations.
        /// Called when an effect is first applied.
        /// </summary>
        /// <param name="grantedTags">The effect's granted tag declarations.</param>
        /// <param name="tagCounts">The target entity's tag count container.</param>
        /// <param name="stackCount">Current stack count of the effect (usually 1 on first apply).</param>
        public static void Grant(in EffectGrantedTags grantedTags, ref TagCountContainer tagCounts, int stackCount, GasBudget budget = null)
        {
            for (int i = 0; i < grantedTags.Count; i++)
            {
                var contribution = grantedTags.Get(i);
                int amount = contribution.Compute(stackCount);
                if (amount > 0)
                {
                    if (!tagCounts.AddCount(contribution.TagId, (ushort)System.Math.Min(amount, ushort.MaxValue)))
                    {
                        if (budget != null) budget.TagCountOverflowDropped++;
                        throw new System.InvalidOperationException("GAS.TAG.ERR.TagCountOverflow");
                    }
                }
            }
        }

        /// <summary>
        /// Revoke tags from the target's <see cref="TagCountContainer"/> when an effect expires or is removed.
        /// </summary>
        /// <param name="grantedTags">The effect's granted tag declarations.</param>
        /// <param name="tagCounts">The target entity's tag count container.</param>
        /// <param name="stackCount">Stack count at the time of removal.</param>
        public static void Revoke(in EffectGrantedTags grantedTags, ref TagCountContainer tagCounts, int stackCount, GasBudget budget = null)
        {
            for (int i = 0; i < grantedTags.Count; i++)
            {
                var contribution = grantedTags.Get(i);
                int amount = contribution.Compute(stackCount);
                if (amount > 0)
                {
                    tagCounts.RemoveCount(contribution.TagId, (ushort)System.Math.Min(amount, ushort.MaxValue));
                }
            }
        }

        /// <summary>
        /// Update tag counts when a stack count changes (e.g. 3 → 5).
        /// Computes delta = newAmount - oldAmount for each tag and adjusts accordingly.
        /// </summary>
        /// <param name="grantedTags">The effect's granted tag declarations.</param>
        /// <param name="tagCounts">The target entity's tag count container.</param>
        /// <param name="oldStackCount">Previous stack count.</param>
        /// <param name="newStackCount">New stack count.</param>
        public static void Update(in EffectGrantedTags grantedTags, ref TagCountContainer tagCounts, int oldStackCount, int newStackCount, GasBudget budget = null)
        {
            for (int i = 0; i < grantedTags.Count; i++)
            {
                var contribution = grantedTags.Get(i);
                int oldAmount = contribution.Compute(oldStackCount);
                int newAmount = contribution.Compute(newStackCount);
                int delta = newAmount - oldAmount;

                if (delta > 0)
                {
                    if (!tagCounts.AddCount(contribution.TagId, (ushort)System.Math.Min(delta, ushort.MaxValue)))
                    {
                        if (budget != null) budget.TagCountOverflowDropped++;
                        throw new System.InvalidOperationException("GAS.TAG.ERR.TagCountOverflow");
                    }
                }
                else if (delta < 0)
                {
                    tagCounts.RemoveCount(contribution.TagId, (ushort)System.Math.Min(-delta, ushort.MaxValue));
                }
            }
        }
    }
}
