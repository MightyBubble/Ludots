using System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public readonly struct TagRuleCompiled
    {
        public readonly GameplayTagContainer RequiredMask;
        public readonly GameplayTagContainer BlockedMask;
        public readonly GameplayTagContainer AttachedMask;
        public readonly GameplayTagContainer RemovedMask;
        public readonly GameplayTagContainer DisabledIfMask;
        public readonly GameplayTagContainer RemoveIfMask;
        public readonly ulong AttachedAny;
        public readonly ulong RemovedAny;
        public readonly ulong DisabledIfAny;
        public readonly ulong RemoveIfAny;

        public TagRuleCompiled(in GameplayTagContainer requiredMask, in GameplayTagContainer blockedMask, in GameplayTagContainer attachedMask, in GameplayTagContainer removedMask, in GameplayTagContainer disabledIfMask, in GameplayTagContainer removeIfMask, ulong attachedAny, ulong removedAny, ulong disabledIfAny, ulong removeIfAny)
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
        public const int MaxCoreTags = 256;

        private readonly TagRuleCompiled[] _compiled = new TagRuleCompiled[MaxCoreTags];
        private readonly ulong[] _hasRuleBits = new ulong[4];

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

            ulong attachedAny = 0;
            ulong removedAny = 0;
            ulong disabledIfAny = 0;
            ulong removeIfAny = 0;
            unsafe
            {
                ulong* a = attachedMask.Bits;
                ulong* r = removedMask.Bits;
                ulong* d = disabledIfMask.Bits;
                ulong* x = removeIfMask.Bits;
                attachedAny = a[0] | a[1] | a[2] | a[3];
                removedAny = r[0] | r[1] | r[2] | r[3];
                disabledIfAny = d[0] | d[1] | d[2] | d[3];
                removeIfAny = x[0] | x[1] | x[2] | x[3];
            }

            _compiled[tagId] = new TagRuleCompiled(in requiredMask, in blockedMask, in attachedMask, in removedMask, in disabledIfMask, in removeIfMask, attachedAny, removedAny, disabledIfAny, removeIfAny);
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

        private static unsafe GameplayTagContainer BuildMask(int* tagIds, int count)
        {
            var mask = default(GameplayTagContainer);

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
