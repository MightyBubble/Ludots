using Arch.Core;
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
        public static void GrantToEntity(World world, Entity target, in EffectGrantedTags grantedTags, int stackCount, TagOps tagOps, GasBudget budget = null)
        {
            if (!world.IsAlive(target) || grantedTags.Count <= 0)
            {
                return;
            }

            EnsureTagState(world, target);
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var dirtyFlags = ref world.Get<DirtyFlags>(target);
            tagOps ??= new TagOps();

            for (int i = 0; i < grantedTags.Count; i++)
            {
                var contribution = grantedTags.Get(i);
                int amount = contribution.Compute(stackCount);
                for (int repeat = 0; repeat < amount; repeat++)
                {
                    tagOps.AddTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags);
                }
            }
        }

        public static void RevokeFromEntity(World world, Entity target, in EffectGrantedTags grantedTags, int stackCount, TagOps tagOps, GasBudget budget = null)
        {
            if (!world.IsAlive(target) || grantedTags.Count <= 0 || !world.Has<TagCountContainer>(target))
            {
                return;
            }

            if (!world.Has<GameplayTagContainer>(target))
            {
                world.Add(target, new GameplayTagContainer());
            }

            if (!world.Has<DirtyFlags>(target))
            {
                world.Add(target, new DirtyFlags());
            }

            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var dirtyFlags = ref world.Get<DirtyFlags>(target);
            tagOps ??= new TagOps();

            for (int i = 0; i < grantedTags.Count; i++)
            {
                var contribution = grantedTags.Get(i);
                int amount = contribution.Compute(stackCount);
                for (int repeat = 0; repeat < amount; repeat++)
                {
                    tagOps.RemoveTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags);
                }
            }
        }

        public static void UpdateOnEntity(World world, Entity target, in EffectGrantedTags grantedTags, int oldStackCount, int newStackCount, TagOps tagOps, GasBudget budget = null)
        {
            if (!world.IsAlive(target) || grantedTags.Count <= 0)
            {
                return;
            }

            EnsureTagState(world, target);
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var dirtyFlags = ref world.Get<DirtyFlags>(target);
            tagOps ??= new TagOps();

            for (int i = 0; i < grantedTags.Count; i++)
            {
                var contribution = grantedTags.Get(i);
                int delta = contribution.Compute(newStackCount) - contribution.Compute(oldStackCount);
                if (delta > 0)
                {
                    for (int repeat = 0; repeat < delta; repeat++)
                    {
                        tagOps.AddTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags);
                    }
                }
                else if (delta < 0)
                {
                    for (int repeat = 0; repeat < -delta; repeat++)
                    {
                        tagOps.RemoveTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags);
                    }
                }
            }
        }

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

        private static void EnsureTagState(World world, Entity target)
        {
            if (!world.Has<GameplayTagContainer>(target))
            {
                world.Add(target, new GameplayTagContainer());
            }

            if (!world.Has<TagCountContainer>(target))
            {
                world.Add(target, new TagCountContainer());
            }

            if (!world.Has<DirtyFlags>(target))
            {
                world.Add(target, new DirtyFlags());
            }
        }
    }
}
