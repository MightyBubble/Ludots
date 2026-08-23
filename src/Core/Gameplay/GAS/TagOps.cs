using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Tag operations with rule-aware add/remove logic.
    /// Non-static: each World should have its own TagOps instance,
    /// injected via GasServices / GameContext.
    /// </summary>
    public class TagOps
    {
        public const string TagCountOverflowError = "GAS.TAG.ERR.TagCountOverflow";
        public const string RuleRejectedError = "GAS.TAG.ERR.RuleRejected";
        public const string MissingTagOpsError = "GAS.TAG.ERR.MissingTagOps";
        public const string MissingGameplayTagContainerError = "GAS.TAG.ERR.MissingGameplayTagContainer";
        public const string MissingTagCountContainerError = "GAS.TAG.ERR.MissingTagCountContainer";
        public const string MissingDirtyFlagsError = "GAS.TAG.ERR.MissingDirtyFlags";

        private readonly TagRuleRegistry _rules;
        private readonly TagRuleTransaction _transaction;
        private readonly GasBudget _budget;
        private readonly DirtyEntityQueue _dirtyEntities;
        private readonly Dictionary<int, TagRuleSet> _authoredRuleSets = new();

        public TagOps(DirtyEntityQueue dirtyEntities, TagRuleRegistry rules, GasBudget budget = null)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _transaction = new TagRuleTransaction();
            _budget = budget;
            _dirtyEntities = dirtyEntities ?? throw new ArgumentNullException(nameof(dirtyEntities));
        }

        /// <summary>
        /// Access the underlying TagRuleRegistry (e.g. for OrderSubmitter).
        /// </summary>
        public TagRuleRegistry Rules => _rules;
        public DirtyEntityQueue DirtyEntities => _dirtyEntities;

        public void MarkDirtyEntity(World world, Entity entity)
        {
            _dirtyEntities.Track(world, entity);
        }

        public bool AddTag(World world, Entity entity, int tagId)
        {
            RequireTagState(world, entity);
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
            ref TagCountContainer counts = ref world.Get<TagCountContainer>(entity);
            ref DirtyFlags dirty = ref world.Get<DirtyFlags>(entity);
            GameplayTagContainer tagsBefore = tags;
            TagCountContainer countsBefore = counts;
            DirtyFlags dirtyBefore = dirty;
            try
            {
                bool changed = AddTag(ref tags, ref counts, tagId, ref dirty);
                if (changed) _dirtyEntities.Track(world, entity);
                return changed;
            }
            catch
            {
                tags = tagsBefore;
                counts = countsBefore;
                dirty = dirtyBefore;
                throw;
            }
        }

        public bool RemoveTag(World world, Entity entity, int tagId)
        {
            RequireTagState(world, entity);
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
            ref TagCountContainer counts = ref world.Get<TagCountContainer>(entity);
            ref DirtyFlags dirty = ref world.Get<DirtyFlags>(entity);
            GameplayTagContainer tagsBefore = tags;
            TagCountContainer countsBefore = counts;
            DirtyFlags dirtyBefore = dirty;
            try
            {
                bool changed = RemoveTag(ref tags, ref counts, tagId, ref dirty);
                if (changed) _dirtyEntities.Track(world, entity);
                return changed;
            }
            catch
            {
                tags = tagsBefore;
                counts = countsBefore;
                dirty = dirtyBefore;
                throw;
            }
        }

        public static void RequireTagState(World world, Entity entity)
        {
            if (!world.IsAlive(entity)) throw new InvalidOperationException(TagStateInstaller.DeadEntityError);
            if (!world.Has<GameplayTagContainer>(entity)) throw new InvalidOperationException(MissingGameplayTagContainerError);
            if (!world.Has<TagCountContainer>(entity)) throw new InvalidOperationException(MissingTagCountContainerError);
            if (!world.Has<DirtyFlags>(entity)) throw new InvalidOperationException(MissingDirtyFlagsError);
        }

        public void ClearRuleRegistry()
        {
            _rules.Clear();
            _authoredRuleSets.Clear();
        }

        public void RegisterTagRuleSet(int tagId, TagRuleSet ruleSet)
        {
            _rules.Register(tagId, ruleSet);
            _authoredRuleSets[tagId] = ruleSet;
        }

        /// <summary>
        /// NextCast-safe replace for an already-registered tag rule identity.
        /// New tag ids (no live rule) require EngineRestart — not hot-applied.
        /// </summary>
        public void ReplaceTagRuleSet(int tagId, TagRuleSet ruleSet)
        {
            if (!_rules.HasRule(tagId))
            {
                throw new InvalidOperationException(
                    $"Tag rule id {tagId} is not registered; cannot ReplaceTagRuleSet (new tag identities require EngineRestart).");
            }

            _rules.Register(tagId, ruleSet);
            _authoredRuleSets[tagId] = ruleSet;
        }

        public bool TryGetAuthoredRuleSet(int tagId, out TagRuleSet ruleSet)
            => _authoredRuleSets.TryGetValue(tagId, out ruleSet);

        public bool HasTagRule(int tagId) => _rules.HasRule(tagId);

        public bool HasTag(ref GameplayTagContainer tagContainer, int tagId, TagSense sense)
        {
            if (sense == TagSense.Present)
            {
                return tagContainer.HasTag(tagId);
            }

            if (!tagContainer.HasTag(tagId))
            {
                return false;
            }

            if (!_rules.HasRule(tagId))
            {
                return true;
            }

            ref readonly var compiled = ref _rules.Get(tagId);
            if (compiled.DisabledIfAny != 0 && tagContainer.Intersects(in compiled.DisabledIfMask))
            {
                return false;
            }

            return true;
        }

        public bool EffectiveMayChangeForDirtyTags(int tagId, in GameplayTagContainer dirtyTags)
        {
            if (tagId <= 0 || (uint)tagId >= TagRuleRegistry.MaxCoreTags)
            {
                return false;
            }

            if (dirtyTags.HasTag(tagId))
            {
                return true;
            }

            if (!_rules.HasRule(tagId))
            {
                return false;
            }

            ref readonly var compiled = ref _rules.Get(tagId);
            return compiled.DisabledIfAny != 0 && dirtyTags.Intersects(in compiled.DisabledIfMask);
        }

        // ── Public API: without DirtyFlags ──

        internal unsafe bool AddTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId)
        {
            return AddTagCore(ref tagContainer, ref countContainer, tagId, dirty: null);
        }

        internal unsafe bool RemoveTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId)
        {
            return RemoveTagCore(ref tagContainer, ref countContainer, tagId, dirty: null);
        }

        // ── Public API: with DirtyFlags ──

        internal bool AddTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, ref DirtyFlags dirtyFlags)
        {
            unsafe
            {
                fixed (DirtyFlags* dp = &dirtyFlags)
                {
                    return AddTagCore(ref tagContainer, ref countContainer, tagId, dp);
                }
            }
        }

        internal bool RemoveTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, ref DirtyFlags dirtyFlags)
        {
            unsafe
            {
                fixed (DirtyFlags* dp = &dirtyFlags)
                {
                    return RemoveTagCore(ref tagContainer, ref countContainer, tagId, dp);
                }
            }
        }

        // ── Unified core implementations ──

        private unsafe bool AddTagCore(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, DirtyFlags* dirty)
        {
            GameplayTagContainer tagsBefore = tagContainer;
            TagCountContainer countsBefore = countContainer;
            DirtyFlags dirtyBefore = dirty == null ? default : *dirty;
            try
            {
                return AddTagCoreTransactional(ref tagContainer, ref countContainer, tagId, dirty);
            }
            catch
            {
                tagContainer = tagsBefore;
                countContainer = countsBefore;
                if (dirty != null) *dirty = dirtyBefore;
                throw;
            }
        }

        private unsafe bool AddTagCoreTransactional(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, DirtyFlags* dirty)
        {
            if (tagId <= 0 || (uint)tagId >= TagRuleRegistry.MaxCoreTags)
                throw new ArgumentOutOfRangeException(nameof(tagId), tagId, $"tagId must be in [1, {TagRuleRegistry.MaxCoreTags - 1}].");

            if (tagContainer.HasTag(tagId))
            {
                if (!countContainer.AddCount(tagId, 1))
                {
                    if (_budget != null) _budget.TagCountOverflowDropped++;
                    throw new InvalidOperationException(TagCountOverflowError);
                }
                MarkDirty(dirty, tagId);
                return true;
            }

            if (!_rules.HasRule(tagId))
            {
                if (!countContainer.AddCount(tagId, 1))
                {
                    if (_budget != null) _budget.TagCountOverflowDropped++;
                    throw new InvalidOperationException(TagCountOverflowError);
                }
                tagContainer.AddTag(tagId);
                MarkDirty(dirty, tagId);
                return true;
            }

            _transaction.Begin();
            try
            {
                bool ok = ExecuteAddTagTransactionCore(ref tagContainer, ref countContainer, tagId, dirty);
                if (ok) MarkDirty(dirty, tagId);
                return ok;
            }
            finally
            {
                _transaction.End();
            }
        }

        private unsafe bool RemoveTagCore(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, DirtyFlags* dirty)
        {
            if (tagId <= 0 || (uint)tagId >= TagRuleRegistry.MaxCoreTags)
                throw new ArgumentOutOfRangeException(nameof(tagId), tagId, $"tagId must be in [1, {TagRuleRegistry.MaxCoreTags - 1}].");

            ushort currentCount = countContainer.GetCount(tagId);
            if (currentCount == 0) return false;

            if (currentCount > 1)
            {
                countContainer.RemoveCount(tagId, 1);
                MarkDirty(dirty, tagId);
                return true;
            }

            countContainer.RemoveCount(tagId, 1);
            tagContainer.RemoveTag(tagId);
            MarkDirty(dirty, tagId);
            return true;
        }

        private unsafe bool ExecuteAddTagTransactionCore(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, DirtyFlags* dirty)
        {
            if (tagContainer.HasTag(tagId)) return true;

            if (!_transaction.TryMarkProcessed(tagId, isAdd: true)) return false;

            if (!CanAddTag(tagId, ref tagContainer)) return false;

            if (!countContainer.AddCount(tagId, 1))
            {
                if (_budget != null) _budget.TagCountOverflowDropped++;
                throw new InvalidOperationException(TagCountOverflowError);
            }
            tagContainer.AddTag(tagId);
            MarkDirty(dirty, tagId);

            ref readonly var compiled = ref _rules.Get(tagId);
            if (compiled.RemovedAny != 0)
            {
                ApplyRemovedCore(in compiled.RemovedMask, ref tagContainer, ref countContainer, dirty);
            }
            if (compiled.AttachedAny != 0)
            {
                ApplyAttachedCore(in compiled.AttachedMask, ref tagContainer, ref countContainer, dirty);
            }
            if (compiled.RemoveIfAny != 0 && tagContainer.Intersects(in compiled.RemoveIfMask))
            {
                RemoveAllInternalCore(tagId, ref tagContainer, ref countContainer, dirty);
            }

            return true;
        }

        private bool CanAddTag(int tagId, ref GameplayTagContainer tagContainer)
        {
            if (!_rules.HasRule(tagId)) return true;

            ref readonly var compiled = ref _rules.Get(tagId);

            if (!tagContainer.ContainsAll(in compiled.RequiredMask)) return false;

            if (tagContainer.Intersects(in compiled.BlockedMask)) return false;

            return true;
        }

        private unsafe void AddTagInternalCore(int tagId, ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, DirtyFlags* dirty)
        {
            if (tagContainer.HasTag(tagId)) return;

            if (!ExecuteAddTagTransactionCore(ref tagContainer, ref countContainer, tagId, dirty)) return;

            MarkDirty(dirty, tagId);
        }

        private unsafe void RemoveAllInternalCore(int tagId, ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, DirtyFlags* dirty)
        {
            if (!tagContainer.HasTag(tagId)) return;

            if (!_transaction.TryMarkProcessed(tagId, isAdd: false)) return;

            tagContainer.RemoveTag(tagId);
            ushort count = countContainer.GetCount(tagId);
            if (count > 0) countContainer.RemoveCount(tagId, count);
            MarkDirty(dirty, tagId);
        }

        private unsafe void ApplyRemovedCore(in GameplayTagContainer removedMask, ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, DirtyFlags* dirty)
        {
            fixed (ulong* removedBits = removedMask.Bits)
            fixed (ulong* presentBits = tagContainer.Bits)
            {
                for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                {
                    ulong bits = removedBits[wordIndex] & presentBits[wordIndex];
                    while (bits != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                        int removedTagId = (wordIndex << 6) + bit;
                        RemoveAllInternalCore(removedTagId, ref tagContainer, ref countContainer, dirty);
                    }
                }
            }
        }

        private unsafe void ApplyAttachedCore(in GameplayTagContainer attachedMask, ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, DirtyFlags* dirty)
        {
            fixed (ulong* attachedBits = attachedMask.Bits)
            fixed (ulong* presentBits = tagContainer.Bits)
            {
                for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                {
                    ulong bits = attachedBits[wordIndex] & ~presentBits[wordIndex];
                    while (bits != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                        int attachedTagId = (wordIndex << 6) + bit;
                        if (!CanAddTag(attachedTagId, ref tagContainer)) continue;
                        AddTagInternalCore(attachedTagId, ref tagContainer, ref countContainer, dirty);
                    }
                }
            }
        }

        // ── Helpers ──

        private static unsafe void MarkDirty(DirtyFlags* dirty, int tagId)
        {
            if (dirty != null) dirty->MarkTagDirty(tagId);
        }

        // ── Multi-tag operations ──

        public bool ContainsAll(ref GameplayTagContainer tagContainer, in GameplayTagContainer required, TagSense sense)
        {
            if (sense == TagSense.Present)
            {
                return tagContainer.ContainsAll(in required);
            }

            if (!tagContainer.ContainsAll(in required))
            {
                return false;
            }

            unsafe
            {
                fixed (ulong* requiredBits = required.Bits)
                {
                    for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                    {
                        ulong bits = requiredBits[wordIndex];
                        while (bits != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(bits);
                            bits &= bits - 1;
                            int tagId = (wordIndex << 6) + bit;
                            if (!HasTag(ref tagContainer, tagId, sense))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        public bool Intersects(ref GameplayTagContainer tagContainer, in GameplayTagContainer other, TagSense sense)
        {
            if (sense == TagSense.Present)
            {
                return tagContainer.Intersects(in other);
            }

            unsafe
            {
                fixed (ulong* otherBits = other.Bits)
                fixed (ulong* presentBits = tagContainer.Bits)
                {
                    for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                    {
                        ulong bits = otherBits[wordIndex] & presentBits[wordIndex];
                        while (bits != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(bits);
                            bits &= bits - 1;
                            int tagId = (wordIndex << 6) + bit;
                            if (HasTag(ref tagContainer, tagId, sense))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}
