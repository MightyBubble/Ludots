using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using System.Numerics;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Tag operations with rule-aware add/remove logic.
    /// Non-static: each World should have its own TagOps instance,
    /// injected via GasServices / GameContext.
    /// </summary>
    public class TagOps
    {

        private readonly TagRuleRegistry _rules;
        private readonly TagRuleTransaction _transaction;
        private readonly GasBudget _budget;

        public TagOps() : this(new TagRuleRegistry(), budget: null) { }

        public TagOps(TagRuleRegistry rules, GasBudget budget = null)
        {
            _rules = rules;
            _transaction = new TagRuleTransaction();
            _budget = budget;
        }

        /// <summary>
        /// Access the underlying TagRuleRegistry (e.g. for OrderSubmitter).
        /// </summary>
        public TagRuleRegistry Rules => _rules;

        public void ClearRuleRegistry()
        {
            _rules.Clear();
        }

        public void RegisterTagRuleSet(int tagId, TagRuleSet ruleSet)
        {
            _rules.Register(tagId, ruleSet);
        }

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

        // ── Public API: without DirtyFlags ──

        public unsafe bool AddTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId)
        {
            return AddTagCore(ref tagContainer, ref countContainer, tagId, dirty: null);
        }

        public unsafe bool RemoveTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId)
        {
            return RemoveTagCore(ref tagContainer, ref countContainer, tagId, dirty: null);
        }

        // ── Public API: with DirtyFlags ──

        public bool AddTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, ref DirtyFlags dirtyFlags)
        {
            unsafe
            {
                fixed (DirtyFlags* dp = &dirtyFlags)
                {
                    return AddTagCore(ref tagContainer, ref countContainer, tagId, dp);
                }
            }
        }

        public bool RemoveTag(ref GameplayTagContainer tagContainer, ref TagCountContainer countContainer, int tagId, ref DirtyFlags dirtyFlags)
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
            if (tagId <= 0 || (uint)tagId >= TagRuleRegistry.MaxCoreTags)
                throw new ArgumentOutOfRangeException(nameof(tagId), tagId, $"tagId must be in [1, {TagRuleRegistry.MaxCoreTags - 1}].");

            if (tagContainer.HasTag(tagId))
            {
                if (!countContainer.AddCount(tagId, 1))
                {
                    if (_budget != null) _budget.TagCountOverflowDropped++;
                    throw new InvalidOperationException("GAS.TAG.ERR.TagCountOverflow");
                }
                MarkDirty(dirty, tagId);
                return true;
            }

            if (!_rules.HasRule(tagId))
            {
                if (!countContainer.AddCount(tagId, 1))
                {
                    if (_budget != null) _budget.TagCountOverflowDropped++;
                    throw new InvalidOperationException("GAS.TAG.ERR.TagCountOverflow");
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
                throw new InvalidOperationException("GAS.TAG.ERR.TagCountOverflow");
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
