using System;
using Ludots.Core.Gameplay.GAS.Capacity;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public readonly struct TagRuleCompiled
    {
        public readonly GameplayTagBitSet RequiredMask;
        public readonly GameplayTagBitSet BlockedMask;
        public readonly GameplayTagBitSet AttachedMask;
        public readonly GameplayTagBitSet RemovedMask;
        public readonly GameplayTagBitSet DisabledIfMask;
        public readonly GameplayTagBitSet RemoveIfMask;
        public readonly ulong AttachedAny;
        public readonly ulong RemovedAny;
        public readonly ulong DisabledIfAny;
        public readonly ulong RemoveIfAny;

        public TagRuleCompiled(
            in GameplayTagBitSet requiredMask,
            in GameplayTagBitSet blockedMask,
            in GameplayTagBitSet attachedMask,
            in GameplayTagBitSet removedMask,
            in GameplayTagBitSet disabledIfMask,
            in GameplayTagBitSet removeIfMask,
            ulong attachedAny,
            ulong removedAny,
            ulong disabledIfAny,
            ulong removeIfAny)
        {
            RequiredMask = requiredMask;
            BlockedMask = blockedMask;
            AttachedMask = attachedMask;
            RemovedMask = removedMask;
            DisabledIfMask = disabledIfMask;
            RemoveIfMask = removeIfMask;
            AttachedAny = attachedAny;
            RemovedAny = removedAny;
            DisabledIfAny = disabledIfAny;
            RemoveIfAny = removeIfAny;
        }
    }

    public sealed class TagRuleRegistry
    {
        public const int MaxCoreTags = GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace;
        private const int HasRuleWordCount = MaxCoreTags / GasLoadTimeCapacityPlan.TagBitsPerWord;

        private readonly TagRuleCompiled[] _compiled = new TagRuleCompiled[MaxCoreTags];
        private readonly ulong[] _hasRuleBits = new ulong[HasRuleWordCount];

        public void Clear()
        {
            Array.Clear(_compiled, 0, _compiled.Length);
            Array.Clear(_hasRuleBits, 0, _hasRuleBits.Length);
        }

        public unsafe void Register(int tagId, TagRuleSet ruleSet)
        {
            if (tagId <= 0 || (uint)tagId >= MaxCoreTags)
            {
                throw new ArgumentOutOfRangeException(nameof(tagId));
            }

            int* requiredTags = ruleSet.RequiredTags;
            int* blockedTags = ruleSet.BlockedTags;
            int* attachedTags = ruleSet.AttachedTags;
            int* removedTags = ruleSet.RemovedTags;
            int* disabledIfTags = ruleSet.DisabledIfTags;
            int* removeIfTags = ruleSet.RemoveIfTags;

            var requiredMask = BuildMask(requiredTags, ruleSet.RequiredCount);
            var blockedMask = BuildMask(blockedTags, ruleSet.BlockedCount);
            var attachedMask = BuildMask(attachedTags, ruleSet.AttachedCount);
            var removedMask = BuildMask(removedTags, ruleSet.RemovedCount);
            var disabledIfMask = BuildMask(disabledIfTags, ruleSet.DisabledIfCount);
            var removeIfMask = BuildMask(removeIfTags, ruleSet.RemoveIfCount);

            _compiled[tagId] = new TagRuleCompiled(
                in requiredMask,
                in blockedMask,
                in attachedMask,
                in removedMask,
                in disabledIfMask,
                in removeIfMask,
                attachedMask.AnyWordBits(),
                removedMask.AnyWordBits(),
                disabledIfMask.AnyWordBits(),
                removeIfMask.AnyWordBits());
            SetHasRule(tagId);
        }

        public bool HasRule(int tagId)
        {
            if ((uint)tagId >= MaxCoreTags)
            {
                return false;
            }

            int word = tagId >> 6;
            int bit = tagId & 63;
            return (_hasRuleBits[word] & (1UL << bit)) != 0;
        }

        public ref readonly TagRuleCompiled Get(int tagId)
        {
            if ((uint)tagId >= MaxCoreTags)
            {
                throw new ArgumentOutOfRangeException(nameof(tagId));
            }

            return ref _compiled[tagId];
        }

        private void SetHasRule(int tagId)
        {
            int word = tagId >> 6;
            int bit = tagId & 63;
            _hasRuleBits[word] |= 1UL << bit;
        }

        private static unsafe GameplayTagBitSet BuildMask(int* tagIds, int count)
        {
            var mask = default(GameplayTagBitSet);

            for (int i = 0; i < count; i++)
            {
                int id = tagIds[i];
                if ((uint)id >= MaxCoreTags)
                {
                    throw new ArgumentOutOfRangeException(nameof(tagIds));
                }

                mask.AddTag(id);
            }

            return mask;
        }
    }
}
