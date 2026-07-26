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
            GrantToEntityCore(world, target, in grantedTags, stackCount, tagOps, budget, trackDirtyEntity: true);
        }

        internal static void PrepareGrantToEntity(World world, Entity target, in EffectGrantedTags grantedTags, int stackCount, TagOps tagOps, GasBudget budget = null)
        {
            GrantToEntityCore(world, target, in grantedTags, stackCount, tagOps, budget, trackDirtyEntity: false);
        }

        private static void GrantToEntityCore(
            World world,
            Entity target,
            in EffectGrantedTags grantedTags,
            int stackCount,
            TagOps tagOps,
            GasBudget budget,
            bool trackDirtyEntity)
        {
            if (!world.IsAlive(target) || grantedTags.Count <= 0)
            {
                return;
            }

            RequireTagState(world, target, tagOps);
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var dirtyFlags = ref world.Get<DirtyFlags>(target);
            GameplayTagContainer tagsBefore = tags;
            TagCountContainer countsBefore = counts;
            DirtyFlags dirtyBefore = dirtyFlags;
            try
            {
                for (int i = 0; i < grantedTags.Count; i++)
                {
                    var contribution = grantedTags.Get(i);
                    int amount = contribution.Compute(stackCount);
                    for (int repeat = 0; repeat < amount; repeat++)
                    {
                        if (!tagOps.AddTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags))
                            throw new System.InvalidOperationException(TagOps.RuleRejectedError);
                    }
                }
                if (trackDirtyEntity && dirtyFlags.IsAnyTagDirty()) tagOps.MarkDirtyEntity(world, target);
            }
            catch
            {
                tags = tagsBefore;
                counts = countsBefore;
                dirtyFlags = dirtyBefore;
                throw;
            }
        }

        public static void RevokeFromEntity(World world, Entity target, in EffectGrantedTags grantedTags, int stackCount, TagOps tagOps, GasBudget budget = null)
        {
            if (!world.IsAlive(target) || grantedTags.Count <= 0)
            {
                return;
            }

            RequireTagState(world, target, tagOps);
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var dirtyFlags = ref world.Get<DirtyFlags>(target);
            GameplayTagContainer tagsBefore = tags;
            TagCountContainer countsBefore = counts;
            DirtyFlags dirtyBefore = dirtyFlags;
            try
            {
                for (int i = 0; i < grantedTags.Count; i++)
                {
                    var contribution = grantedTags.Get(i);
                    int amount = contribution.Compute(stackCount);
                    for (int repeat = 0; repeat < amount; repeat++)
                        tagOps.RemoveTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags);
                }
                if (dirtyFlags.IsAnyTagDirty()) tagOps.MarkDirtyEntity(world, target);
            }
            catch
            {
                tags = tagsBefore;
                counts = countsBefore;
                dirtyFlags = dirtyBefore;
                throw;
            }
        }

        public static void UpdateOnEntity(World world, Entity target, in EffectGrantedTags grantedTags, int oldStackCount, int newStackCount, TagOps tagOps, GasBudget budget = null)
        {
            if (!world.IsAlive(target) || grantedTags.Count <= 0)
            {
                return;
            }

            RequireTagState(world, target, tagOps);
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var dirtyFlags = ref world.Get<DirtyFlags>(target);
            GameplayTagContainer tagsBefore = tags;
            TagCountContainer countsBefore = counts;
            DirtyFlags dirtyBefore = dirtyFlags;
            try
            {
                for (int i = 0; i < grantedTags.Count; i++)
                {
                    var contribution = grantedTags.Get(i);
                    int delta = contribution.Compute(newStackCount) - contribution.Compute(oldStackCount);
                    if (delta > 0)
                    {
                        for (int repeat = 0; repeat < delta; repeat++)
                            if (!tagOps.AddTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags))
                                throw new System.InvalidOperationException(TagOps.RuleRejectedError);
                    }
                    else if (delta < 0)
                    {
                        for (int repeat = 0; repeat < -delta; repeat++)
                            tagOps.RemoveTag(ref tags, ref counts, contribution.TagId, ref dirtyFlags);
                    }
                }
                if (dirtyFlags.IsAnyTagDirty()) tagOps.MarkDirtyEntity(world, target);
            }
            catch
            {
                tags = tagsBefore;
                counts = countsBefore;
                dirtyFlags = dirtyBefore;
                throw;
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
            TagCountContainer before = tagCounts;
            try
            {
                for (int i = 0; i < grantedTags.Count; i++)
                {
                    var contribution = grantedTags.Get(i);
                    int amount = contribution.Compute(stackCount);
                    if (amount > 0 && !tagCounts.AddCount(contribution.TagId, (ushort)System.Math.Min(amount, ushort.MaxValue)))
                    {
                        if (budget != null) budget.TagCountOverflowDropped++;
                        throw new System.InvalidOperationException(TagOps.TagCountOverflowError);
                    }
                }
            }
            catch { tagCounts = before; throw; }
        }

        /// <summary>
        /// Revoke tags from the target's <see cref="TagCountContainer"/> when an effect expires or is removed.
        /// </summary>
        /// <param name="grantedTags">The effect's granted tag declarations.</param>
        /// <param name="tagCounts">The target entity's tag count container.</param>
        /// <param name="stackCount">Stack count at the time of removal.</param>
        public static void Revoke(in EffectGrantedTags grantedTags, ref TagCountContainer tagCounts, int stackCount, GasBudget budget = null)
        {
            TagCountContainer before = tagCounts;
            try
            {
                for (int i = 0; i < grantedTags.Count; i++)
                {
                    var contribution = grantedTags.Get(i);
                    int amount = contribution.Compute(stackCount);
                    if (amount > 0) tagCounts.RemoveCount(contribution.TagId, (ushort)System.Math.Min(amount, ushort.MaxValue));
                }
            }
            catch { tagCounts = before; throw; }
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
            TagCountContainer before = tagCounts;
            try
            {
                for (int i = 0; i < grantedTags.Count; i++)
                {
                    var contribution = grantedTags.Get(i);
                    int delta = contribution.Compute(newStackCount) - contribution.Compute(oldStackCount);
                    if (delta > 0 && !tagCounts.AddCount(contribution.TagId, (ushort)System.Math.Min(delta, ushort.MaxValue)))
                    {
                        if (budget != null) budget.TagCountOverflowDropped++;
                        throw new System.InvalidOperationException(TagOps.TagCountOverflowError);
                    }
                    else if (delta < 0) tagCounts.RemoveCount(contribution.TagId, (ushort)System.Math.Min(-delta, ushort.MaxValue));
                }
            }
            catch { tagCounts = before; throw; }
        }

        private static void RequireTagState(World world, Entity target, TagOps tagOps)
        {
            if (tagOps == null) throw new System.InvalidOperationException(TagOps.MissingTagOpsError);
            if (!world.Has<GameplayTagContainer>(target)) throw new System.InvalidOperationException(TagOps.MissingGameplayTagContainerError);
            if (!world.Has<TagCountContainer>(target)) throw new System.InvalidOperationException(TagOps.MissingTagCountContainerError);
            if (!world.Has<DirtyFlags>(target)) throw new System.InvalidOperationException(TagOps.MissingDirtyFlagsError);
        }
    }
}
